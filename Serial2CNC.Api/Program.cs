using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Serial2CNC.Api.Logs;
using Serial2CNC.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();

var agentBaseUrl = builder.Configuration["Agent:BaseUrl"] ?? "http://localhost:62904";

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();
builder.Services.AddSingleton<LogBroadcastService>();
builder.Services.AddHostedService<AgentLogBridgeService>();

builder.Services.AddHttpClient("agent", client =>
{
    client.BaseAddress = new Uri(agentBaseUrl);
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Redirect("/swagger"));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/transfers/state", async (IHttpClientFactory factory, CancellationToken cancellationToken) =>
{
    var client = factory.CreateClient("agent");
    var response = await client.GetAsync("/transfers/state", cancellationToken);
    var payload = await response.Content.ReadAsStringAsync(cancellationToken);

    return Results.Content(payload, "application/json", statusCode: (int)response.StatusCode);
});

app.MapGet("/api/settings", async (IHttpClientFactory factory, CancellationToken cancellationToken) =>
{
    var client = factory.CreateClient("agent");
    var response = await client.GetAsync("/settings", cancellationToken);
    var payload = await response.Content.ReadAsStringAsync(cancellationToken);

    return Results.Content(payload, "application/json", statusCode: (int)response.StatusCode);
});

app.MapPut("/api/settings", async (UpdateServiceSettingsRequest request, IHttpClientFactory factory, CancellationToken cancellationToken) =>
{
    var client = factory.CreateClient("agent");
    using var response = await client.PutAsJsonAsync("/settings", request, cancellationToken);
    var payload = await response.Content.ReadAsStringAsync(cancellationToken);

    return Results.Content(payload, response.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)response.StatusCode);
})
.Produces<ServiceSettingsDto>(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status403Forbidden);

app.MapGet("/api/files/children", async (string? path, IHttpClientFactory factory, CancellationToken cancellationToken) =>
{
    var client = factory.CreateClient("agent");
    var url = string.IsNullOrWhiteSpace(path)
        ? "/files/children"
        : $"/files/children?path={Uri.EscapeDataString(path)}";

    using var response = await client.GetAsync(url, cancellationToken);
    var payload = await response.Content.ReadAsStringAsync(cancellationToken);

    return Results.Content(payload, response.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)response.StatusCode);
})
.Produces<IReadOnlyList<FileTreeNodeDto>>(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status403Forbidden);

app.MapPost("/api/transfers/start", async (TransferRequest request, IHttpClientFactory factory, LogBroadcastService logs, CancellationToken cancellationToken) =>
{
    await logs.BroadcastAsync("Api", "Information", $"Forwarding start request for file '{request.Transfer.FilePath}'.", cancellationToken);
    var client = factory.CreateClient("agent");
    var response = await client.PostAsJsonAsync("/transfers/start", request, cancellationToken);
    var payload = await response.Content.ReadAsStringAsync(cancellationToken);

    await logs.BroadcastAsync("Api", response.IsSuccessStatusCode ? "Information" : "Warning", $"Start request returned {(int)response.StatusCode}.", cancellationToken);

    return Results.Content(payload, "application/json", statusCode: (int)response.StatusCode);
});

app.MapPost("/api/transfers/cancel", async (IHttpClientFactory factory, LogBroadcastService logs, CancellationToken cancellationToken) =>
{
    await logs.BroadcastAsync("Api", "Information", "Forwarding cancel request.", cancellationToken);
    var client = factory.CreateClient("agent");
    var response = await client.PostAsync("/transfers/cancel", null, cancellationToken);
    var payload = await response.Content.ReadAsStringAsync(cancellationToken);

    await logs.BroadcastAsync("Api", response.IsSuccessStatusCode ? "Information" : "Warning", $"Cancel request returned {(int)response.StatusCode}.", cancellationToken);

    return Results.Content(payload, "application/json", statusCode: (int)response.StatusCode);
});

app.MapHub<LogStreamHub>("/hubs/logs");

app.Run();
