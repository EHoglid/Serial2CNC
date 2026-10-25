using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.FluentUI.AspNetCore.Components;
using Serial2CNC.Agent;
using Serial2CNC.Agent.Middleware;

namespace Serial2CNC.Web;

public static class WebHostFactory
{
    public static WebApplication Create(string[] args, string? desktopDataDirectory = null, int desktopPort = 62899)
    {
        var embedded = desktopDataDirectory is not null;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(App).Assembly.GetName().Name,
            ContentRootPath = embedded ? AppContext.BaseDirectory : null
        });
        builder.WebHost.UseStaticWebAssets();
        if (embedded)
        {
            builder.WebHost.UseUrls($"http://0.0.0.0:{desktopPort}");
        }
        else if (string.IsNullOrWhiteSpace(builder.Configuration["urls"])
            && !builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
        {
            builder.WebHost.UseUrls("http://0.0.0.0:8080");
        }

        var dataDirectory = desktopDataDirectory ?? builder.Configuration["DataDirectory"]
            ?? Path.Combine(AppContext.BaseDirectory, "data");
        builder.Services.AddSingleton(new HostCapabilities(embedded));
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<TransferSessionService>();
        builder.Services.AddSingleton(provider => new ServiceSettingsStore(
            dataDirectory, provider.GetRequiredService<ILogger<ServiceSettingsStore>>()));
        builder.Services.AddExceptionHandler<FileAccessExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.AddFluentUIComponents();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddHttpClient("api", (provider, client) =>
        {
            var addresses = provider.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
            var address = addresses?.Addresses.FirstOrDefault()
                ?? throw new InvalidOperationException("The web application has no listening address.");
            client.BaseAddress = new Uri(address.Replace("0.0.0.0", "127.0.0.1").Replace("[::]", "127.0.0.1"));
            client.Timeout = TimeSpan.FromSeconds(12);
        });
        builder.Services.AddScoped<TransferApiClient>();
        builder.Services.AddScoped<LiveLogStreamClient>();

        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseStaticFiles();
        app.UseAntiforgery();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapTransferEndpoints(allowSerial: embedded);
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        return app;
    }
}

public sealed record HostCapabilities(bool AllowSerial);
