using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Serial2CNC.Security;

public static class ServiceSecurity
{
    public const string HeaderName = "X-Serial2CNC-Key";
    public const string ActorHeader = "X-Serial2CNC-User";
    private const string Scheme = "ServiceKey";

    public static string LoadSecurityConfiguration(this WebApplicationBuilder builder, string component, string[] args)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Serial2CNC", component);
        var path = builder.Configuration["SecurityConfigPath"] ?? Path.Combine(directory, "security.json");
        builder.Configuration.AddJsonFile(path, optional: true, reloadOnChange: false);
        // Explicit environment/command-line overrides still take precedence over the protected file.
        builder.Configuration.AddEnvironmentVariables().AddCommandLine(args);
        return builder.Configuration["DataDirectory"] ?? directory;
    }

    public static string RequireKey(this IConfiguration configuration, string name)
    {
        var key = configuration[name];
        if (string.IsNullOrWhiteSpace(key) || !Convert.TryFromBase64String(key, new byte[key.Length], out var length) || length < 32)
        {
            throw new InvalidOperationException($"{name} must contain a random Base64 key of at least 32 bytes. Run setup-security.ps1.");
        }
        return key;
    }

    public static Uri RequirePrivateServiceUrl(this IConfiguration configuration, string name, string fallback)
    {
        var uri = new Uri(configuration[name] ?? fallback, UriKind.Absolute);
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        {
            throw new InvalidOperationException($"{name} must use HTTPS unless the destination is loopback.");
        }
        return uri;
    }

    public static void AddServiceAuthentication(this WebApplicationBuilder builder, string defaultUrl)
    {
        _ = builder.Configuration.RequireKey("ServiceAuthentication:ApiKey");
        if (string.IsNullOrWhiteSpace(builder.Configuration["urls"])
            && !builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
        {
            builder.WebHost.UseSetting("urls", defaultUrl);
        }
        builder.Services.AddAuthentication(Scheme).AddScheme<AuthenticationSchemeOptions, ServiceKeyHandler>(Scheme, _ => { });
        builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(
            new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
    }
}

internal sealed class ServiceKeyHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ServiceSecurity.HeaderName, out var supplied))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }
        var expected = configuration.RequireKey("ServiceAuthentication:ApiKey");
        if (supplied.Count != 1 || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(supplied.ToString())),
                SHA256.HashData(Encoding.UTF8.GetBytes(expected))))
        {
            Logger.LogWarning("Rejected invalid service credential from {RemoteAddress}.", Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("Invalid service credential."));
        }
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "service")], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        await Response.WriteAsJsonAsync(new { title = "Service authentication required", status = 401 }, Context.RequestAborted);
    }
}
