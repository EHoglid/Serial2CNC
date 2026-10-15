using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Serial2CNC.Security;
using Serial2CNC.Web.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Serial2CNC.Models;
using Xunit;

namespace Serial2CNC.Tests;

public sealed class AuthenticationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-random-key")]
    [InlineData("c2hvcnQ=")]
    public void MissingOrWeakServiceKeysFailClosed(string? key)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ServiceAuthentication:ApiKey"] = key }).Build();
        Assert.Throws<InvalidOperationException>(() => configuration.RequireKey("ServiceAuthentication:ApiKey"));
    }

    [Theory]
    [InlineData("http://192.168.1.5:62904", false)]
    [InlineData("http://127.0.0.1:62904", true)]
    [InlineData("https://agent.example.test:62904", true)]
    public void ServiceCredentialsCannotTravelOverLanHttp(string url, bool allowed)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Agent:BaseUrl"] = url }).Build();
        if (allowed) Assert.Equal(url, configuration.RequirePrivateServiceUrl("Agent:BaseUrl", url).ToString().TrimEnd('/'));
        else Assert.Throws<InvalidOperationException>(() => configuration.RequirePrivateServiceUrl("Agent:BaseUrl", url));
    }

    [Fact]
    public async Task AgentRejectsTransfersOutsideConfiguredFolderOrExtensionFilter()
    {
        await using var factory = new SecureFactory<Serial2CNC.Agent.TransferHub>();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ServiceSecurity.HeaderName, factory.Key);
        var programDirectory = Path.Combine(factory.DataDirectory, "programs");
        Directory.CreateDirectory(programDirectory);
        await File.WriteAllTextAsync(Path.Combine(factory.DataDirectory, "outside.nc"), "test", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(programDirectory, "not-allowed.txt"), "test", TestContext.Current.CancellationToken);
        var settings = await client.PutAsJsonAsync("/settings", new { workingDirectory = programDirectory, fileExtensionFilter = "*.nc" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        foreach (var file in new[] { Path.Combine(factory.DataDirectory, "outside.nc"), Path.Combine(programDirectory, "not-allowed.txt") })
        {
            var response = await client.PostAsJsonAsync("/transfers/start", new { transfer = new { filePath = file } }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task AgentRequiresItsOwnKeyForEndpointsAndHub()
    {
        await using var factory = new SecureFactory<Serial2CNC.Agent.TransferHub>();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/transfers/state", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/hubs/transfers/negotiate?negotiateVersion=1", null, TestContext.Current.CancellationToken)).StatusCode);
        client.DefaultRequestHeaders.Add(ServiceSecurity.HeaderName, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/settings", TestContext.Current.CancellationToken)).StatusCode);
        client.DefaultRequestHeaders.Remove(ServiceSecurity.HeaderName);
        client.DefaultRequestHeaders.Add(ServiceSecurity.HeaderName, factory.Key);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/transfers/state", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/hubs/transfers/negotiate?negotiateVersion=1", null, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task ApiRequiresAuthenticationIncludingItsLogHub()
    {
        await using var factory = new SecureFactory<Serial2CNC.Api.Logs.LogStreamHub>();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/settings", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/hubs/logs/negotiate?negotiateVersion=1", null, TestContext.Current.CancellationToken)).StatusCode);
        client.DefaultRequestHeaders.Add(ServiceSecurity.HeaderName, factory.Key);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/hubs/logs/negotiate?negotiateVersion=1", null, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task WebLoginProtectsPagesAndUsesAntiforgeryAndSecureCookies()
    {
        await using var factory = new SecureFactory<Serial2CNC.Web.App>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["UserName"] = "admin", ["Password"] = "invalid" }), TestContext.Current.CancellationToken)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser("admin");
            AccountSetup.Ensure(await users.CreateAsync(user, "Initial-Test-Password1!"));
            AccountSetup.Ensure(await users.AddToRoleAsync(user, AccountRoles.Administrator));
        }
        var html = await client.GetStringAsync("/Account/Login", TestContext.Current.CancellationToken);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = "admin", ["Password"] = "Initial-Test-Password1!", ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token)
        }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), value => value.StartsWith("__Host-Serial2CNC="));
        Assert.Contains("secure", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=strict", cookie);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Account/Users", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
        using var insecure = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.BadRequest, (await insecure.GetAsync("/Account/Login", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task FailedLoginLocksAccountAfterFiveAttempts()
    {
        await using var factory = new SecureFactory<Serial2CNC.Web.App>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            AccountSetup.Ensure(await users.CreateAsync(new IdentityUser("locked"), "Initial-Test-Password1!"));
        }
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var token = await GetTokenAsync(client, "/Account/Login");
            var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["UserName"] = "locked", ["Password"] = "wrong-password", ["__RequestVerificationToken"] = token
            }), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Sign-in failed.", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
        using var checkScope = factory.Services.CreateScope();
        var manager = checkScope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        Assert.True(await manager.IsLockedOutAsync((await manager.FindByNameAsync("locked"))!));
    }

    [Fact]
    public async Task AdministratorCanCreateAccountsButViewerCannotManageThem()
    {
        await using var factory = new SecureFactory<Serial2CNC.Web.App>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var admin = new IdentityUser("admin");
            AccountSetup.Ensure(await users.CreateAsync(admin, "Initial-Test-Password1!"));
            AccountSetup.Ensure(await users.AddToRoleAsync(admin, AccountRoles.Administrator));
        }
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, "/Account/Login", new()
        {
            ["UserName"] = "admin", ["Password"] = "Initial-Test-Password1!"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, "/Account/Users", new()
        {
            ["UserName"] = "viewer", ["NewPassword"] = "Viewer-Test-Password1!", ["Role"] = AccountRoles.Viewer
        }, "?handler=Create")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var viewer = await users.FindByNameAsync("viewer");
            Assert.NotNull(viewer);
            Assert.True(await users.IsInRoleAsync(viewer, AccountRoles.Viewer));
        }
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, "/Account/Logout", new())).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, "/Account/Login", new()
        {
            ["UserName"] = "viewer", ["Password"] = "Viewer-Test-Password1!"
        })).StatusCode);
        var denied = await client.GetAsync("/Account/Users", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("https://localhost/Account/AccessDenied", denied.Headers.Location?.AbsoluteUri);
    }

    private static async Task<string> GetTokenAsync(HttpClient client, string page)
    {
        var html = await client.GetStringAsync(page, TestContext.Current.CancellationToken);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        return WebUtility.HtmlDecode(token);
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string page, Dictionary<string, string> values, string handler = "")
    {
        values["__RequestVerificationToken"] = await GetTokenAsync(client, page);
        return await client.PostAsync(page + handler, new FormUrlEncodedContent(values), TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(AccountRoles.Viewer, AccountRoles.Viewer, true)]
    [InlineData(AccountRoles.Viewer, AccountRoles.Operator, false)]
    [InlineData(AccountRoles.Operator, AccountRoles.Administrator, false)]
    [InlineData(AccountRoles.Operator, AccountRoles.Operator, true)]
    [InlineData(AccountRoles.Administrator, AccountRoles.Operator, true)]
    [InlineData(AccountRoles.Administrator, AccountRoles.Administrator, true)]
    public async Task ServerSideAuthorizationUsesCurrentRoleAndRejectsRevokedSessions(string actualRole, string requiredRole, bool allowed)
    {
        await using var factory = new SecureFactory<Serial2CNC.Web.App>();
        _ = factory.CreateClient();
        ClaimsPrincipal principal;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser("testuser");
            AccountSetup.Ensure(await users.CreateAsync(user, "Initial-Test-Password1!"));
            AccountSetup.Ensure(await users.AddToRoleAsync(user, actualRole));
            principal = await scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<IdentityUser>>().CreateAsync(user);
            ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(AccountRoles.SessionExpiry,
                DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString()));
        }
        var access = new UserAccess(new TestAuthenticationState(principal), factory.Services.GetRequiredService<IServiceScopeFactory>());
        if (allowed) Assert.Same(principal, await access.RequireAsync(requiredRole));
        else
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<HttpRequestException>(() => access.RequireAsync(requiredRole))).StatusCode);
            var api = new Web.TransferApiClient(factory.Services.GetRequiredService<IHttpClientFactory>(), access,
                NullLogger<Web.TransferApiClient>.Instance);
            Task deniedAction = requiredRole == AccountRoles.Operator
                ? api.CancelAsync(TestContext.Current.CancellationToken)
                : api.UpdateSettingsAsync(new UpdateServiceSettingsRequest(), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<HttpRequestException>(async () => await deniedAction)).StatusCode);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = await users.GetUserAsync(principal);
            AccountSetup.Ensure(await users.UpdateSecurityStampAsync(user!));
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await Assert.ThrowsAsync<HttpRequestException>(() => access.RequireAsync())).StatusCode);
    }

    [Fact]
    public async Task AccountRequestsAreRateLimitedAtThirtyPerMinute()
    {
        await using var factory = new SecureFactory<Web.App>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        for (var request = 0; request < 30; request++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Account/Login", TestContext.Current.CancellationToken)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/Account/Login", TestContext.Current.CancellationToken)).StatusCode);
    }

    private sealed class TestAuthenticationState(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(principal));
    }

    private sealed class SecureFactory<T> : WebApplicationFactory<T> where T : class
    {
        public string Key { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "Serial2CNC-auth-tests-" + Guid.NewGuid());
        public string DataDirectory => _directory;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("DataDirectory", _directory);
            builder.UseSetting("ServiceAuthentication:ApiKey", Key);
            builder.UseSetting("Agent:ApiKey", Key);
            builder.UseSetting("Api:ApiKey", Key);
            builder.UseSetting("Agent:BaseUrl", "http://127.0.0.1:1");
            builder.UseSetting("Api:BaseUrl", "http://127.0.0.1:1");
            builder.UseSetting("SecurityConfigPath", Path.Combine(_directory, "absent.json"));
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
