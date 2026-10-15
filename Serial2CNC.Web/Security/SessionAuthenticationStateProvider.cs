using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Serial2CNC.Web.Security;

public sealed class SessionAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityOptions> options) : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(30);

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = await users.GetUserAsync(authenticationState.User);
        return user is not null && !await users.IsLockedOutAsync(user)
            && user.SecurityStamp == authenticationState.User.FindFirst(options.Value.ClaimsIdentity.SecurityStampClaimType)?.Value
            && long.TryParse(authenticationState.User.FindFirst(AccountRoles.SessionExpiry)?.Value, out var expiry)
            && expiry > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
