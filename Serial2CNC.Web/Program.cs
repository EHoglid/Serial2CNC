using Serial2CNC.Web;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serial2CNC.Security;
using Serial2CNC.Web.Security;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Hosting;

var hostArgs = args.Where((argument, index) =>
    argument is not ("--create-admin" or "--has-admin") && (index == 0 || args[index - 1] != "--create-admin")).ToArray();
var builder = WebApplication.CreateBuilder(hostArgs);
var dataDirectory = builder.LoadSecurityConfiguration("Web", hostArgs);
builder.Host.UseWindowsService();

Directory.CreateDirectory(dataDirectory);
builder.Services.AddDbContext<AccountDbContext>(options =>
    options.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(dataDirectory, "auth.db")
    }.ToString()));
var dataProtection = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "DataProtection")))
    .SetApplicationName("Serial2CNC.Web");
if (OperatingSystem.IsWindows())
{
    dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
}
builder.Services.AddIdentityCore<IdentityUser>(options =>
{
    options.Password.RequiredLength = 12;
    options.User.RequireUniqueEmail = false;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AccountDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.Name = "__Host-Serial2CNC";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
});
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.Zero;
    options.OnRefreshingPrincipal = context =>
    {
        var expiry = context.CurrentPrincipal?.FindFirst(AccountRoles.SessionExpiry);
        if (expiry is not null && context.NewPrincipal?.Identity is System.Security.Claims.ClaimsIdentity identity)
        {
            identity.AddClaim(expiry);
        }
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(
    new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, SessionAuthenticationStateProvider>();
builder.Services.AddScoped<UserAccess>();
builder.Services.AddRazorPages().AddMvcOptions(options =>
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("accounts", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddFluentUIComponents();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var apiBaseUrl = builder.Configuration.RequirePrivateServiceUrl("Api:BaseUrl", "http://127.0.0.1:62901");
var apiKey = builder.Configuration.RequireKey("Api:ApiKey");
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"])
    && !builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
{
    builder.WebHost.UseSetting("urls", "https://localhost:62899");
}

builder.Services.AddTransient<ApiConnectionHandler>();
builder.Services.AddHttpClient("api", client =>
{
    client.BaseAddress = apiBaseUrl;
    client.DefaultRequestHeaders.Add(ServiceSecurity.HeaderName, apiKey);
    client.Timeout = TimeSpan.FromSeconds(12);
}).AddHttpMessageHandler<ApiConnectionHandler>();

builder.Services.AddScoped<TransferApiClient>();
builder.Services.AddScoped<LiveLogStreamClient>();

var app = builder.Build();
var setupExitCode = await AccountSetup.InitializeAsync(app.Services, args);
if (args.Contains("--create-admin") || args.Contains("--has-admin"))
{
    Environment.ExitCode = setupExitCode;
    return;
}
app.Use(async (context, next) =>
{
    if (!context.Request.IsHttps && context.Request.Path != "/health")
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("HTTPS is required.", context.RequestAborted);
        return;
    }
    await next(context);
});
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
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapRazorPages().RequireRateLimiting("accounts");
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
