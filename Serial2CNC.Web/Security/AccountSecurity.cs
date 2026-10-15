using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Serial2CNC.Web.Security;

public sealed class AccountDbContext(DbContextOptions<AccountDbContext> options)
    : IdentityDbContext<IdentityUser>(options);

public static class AccountRoles
{
    public const string Viewer = "Viewer";
    public const string Operator = "Operator";
    public const string Administrator = "Administrator";
    public static readonly string[] All = [Viewer, Operator, Administrator];
    public const string SessionExpiry = "serial2cnc:expires";
}

public sealed class UserAccess(
    AuthenticationStateProvider authenticationState,
    IServiceScopeFactory scopeFactory)
{
    public async Task<ClaimsPrincipal> RequireAsync(string role = AccountRoles.Viewer)
    {
        var principal = (await authenticationState.GetAuthenticationStateAsync()).User;
        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = await users.GetUserAsync(principal);
        if (principal.Identity?.IsAuthenticated != true || user is null
            || await users.IsLockedOutAsync(user)
            || principal.FindFirstValue(new IdentityOptions().ClaimsIdentity.SecurityStampClaimType) != user.SecurityStamp
            || !long.TryParse(principal.FindFirstValue(AccountRoles.SessionExpiry), out var expiry)
            || DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expiry)
        {
            throw new HttpRequestException("Your session has expired. Sign in again.", null, System.Net.HttpStatusCode.Unauthorized);
        }
        var roles = await users.GetRolesAsync(user);
        var allowed = roles.Contains(AccountRoles.Administrator)
            || (role == AccountRoles.Viewer && roles.Any(AccountRoles.All.Contains))
            || roles.Contains(role);
        if (!allowed)
        {
            throw new HttpRequestException($"This action requires the {role} role.", null, System.Net.HttpStatusCode.Forbidden);
        }
        return principal;
    }
}
