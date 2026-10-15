using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Serial2CNC.Web.Security;

public static class AccountSetup
{
    public static async Task<int> InitializeAsync(IServiceProvider services, string[] args)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountDbContext>();
        await db.Database.EnsureCreatedAsync();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in AccountRoles.All)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                Ensure(await roles.CreateAsync(new IdentityRole(role)));
            }
        }
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        if (args.Contains("--has-admin"))
        {
            return (await users.GetUsersInRoleAsync(AccountRoles.Administrator)).Count > 0 ? 0 : 3;
        }
        var index = Array.IndexOf(args, "--create-admin");
        if (index < 0)
        {
            return 0;
        }
        if (index + 1 >= args.Length)
        {
            throw new InvalidOperationException("--create-admin requires a username; supply its password through standard input.");
        }
        // Bootstrap is deliberately one-time. Ongoing account management requires an authenticated administrator.
        if (await users.Users.AnyAsync())
        {
            throw new InvalidOperationException("Accounts already exist. Use the authenticated Accounts page.");
        }
        var password = await Console.In.ReadLineAsync()
            ?? throw new InvalidOperationException("A password is required on standard input.");
        var user = new IdentityUser(args[index + 1]);
        await using var transaction = await db.Database.BeginTransactionAsync();
        Ensure(await users.CreateAsync(user, password));
        Ensure(await users.AddToRoleAsync(user, AccountRoles.Administrator));
        await transaction.CommitAsync();
        Console.WriteLine("Administrator created.");
        return 0;
    }

    public static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(error => error.Description)));
        }
    }
}
