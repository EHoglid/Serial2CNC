using Serial2CNC.Web;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();

builder.Services.AddFluentUIComponents();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var apiBaseUrl = builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5220";

builder.Services.AddTransient<ApiConnectionHandler>();
builder.Services.AddHttpClient("api", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
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

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
