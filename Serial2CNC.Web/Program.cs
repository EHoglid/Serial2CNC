using Serial2CNC.Web;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;

var builder = WebApplication.CreateBuilder(args);
var dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Serial2CNC");
var serviceConfigPath = builder.Configuration["ServiceConfigPath"] ??
    Path.Combine(dataRoot, "Web", "settings.json");
builder.Configuration.AddJsonFile(serviceConfigPath, optional: true, reloadOnChange: false);
builder.Configuration.AddEnvironmentVariables().AddCommandLine(args);
builder.Host.UseWindowsService();

builder.Services.AddFluentUIComponents();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var apiBaseUrl = new Uri(builder.Configuration["Api:BaseUrl"] ?? "http://127.0.0.1:62901");
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"])
    && !builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
{
    builder.WebHost.UseSetting("urls", "https://localhost:62899");
}

builder.Services.AddTransient<ApiConnectionHandler>();
builder.Services.AddHttpClient("api", client =>
{
    client.BaseAddress = apiBaseUrl;
    client.Timeout = TimeSpan.FromSeconds(12);
}).AddHttpMessageHandler<ApiConnectionHandler>();

builder.Services.AddScoped<TransferApiClient>();
builder.Services.AddScoped<LiveLogStreamClient>();

var app = builder.Build();
var isRunningInContainer = string.Equals(
    Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
    "true",
    StringComparison.OrdinalIgnoreCase);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

if (!isRunningInContainer)
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
