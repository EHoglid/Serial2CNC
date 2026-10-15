using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Serial2CNC.Web.Security;

namespace Serial2CNC.Web.Pages.Account;

[Authorize(Roles = AccountRoles.Administrator)]
public sealed class UsersModel(UserManager<IdentityUser> users, AccountDbContext db, ILogger<UsersModel> logger) : PageModel
{
    private static readonly SemaphoreSlim AccountChanges = new(1, 1);
    public List<AccountRow> Accounts { get; } = [];
    public sealed record AccountRow(string Id, string Name, string Role, bool Disabled);
    [BindProperty] public string UserName { get; set; } = "";
    [BindProperty] public string NewPassword { get; set; } = "";
    [BindProperty] public string Role { get; set; } = AccountRoles.Viewer;
    [BindProperty] public string UserId { get; set; } = "";
    [BindProperty] public bool Disabled { get; set; }

    public async Task OnGetAsync()
    {
        foreach (var user in await users.Users.OrderBy(user => user.UserName).ToListAsync())
        {
            Accounts.Add(new(user.Id, user.UserName ?? user.Id,
                (await users.GetRolesAsync(user)).Single(), await users.IsLockedOutAsync(user)));
        }
    }

    public Task<IActionResult> OnPostCreateAsync() => ChangeAsync(async () =>
    {
        if (!AccountRoles.All.Contains(Role) || string.IsNullOrWhiteSpace(UserName) || UserName.Length > 256)
        {
            ModelState.AddModelError("", "Choose a valid username and role.");
            return;
        }
        if (!ValidatePassword()) return;
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = new IdentityUser(UserName);
        if (!Check(await users.CreateAsync(user, NewPassword))) return;
        if (!Check(await users.AddToRoleAsync(user, Role))) return;
        await transaction.CommitAsync();
        logger.LogInformation("Administrator {Admin} created account {User} with role {Role}.", User.Identity?.Name, UserName, Role);
    });

    public Task<IActionResult> OnPostUpdateAsync() => ChangeAsync(async () =>
    {
        var user = await users.FindByIdAsync(UserId);
        if (user is null || !AccountRoles.All.Contains(Role))
        {
            ModelState.AddModelError("", "Choose an existing account and a valid role.");
            return;
        }
        if (user.Id == users.GetUserId(User))
        {
            ModelState.AddModelError("", "You cannot disable or change your own role.");
            return;
        }
        // Self-editing is forbidden, so the acting administrator always remains available.
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (!Check(await users.RemoveFromRolesAsync(user, await users.GetRolesAsync(user)))) return;
        if (!Check(await users.AddToRoleAsync(user, Role))) return;
        if (!Check(await users.SetLockoutEndDateAsync(user, Disabled ? DateTimeOffset.MaxValue : null))) return;
        if (!Disabled && !Check(await users.ResetAccessFailedCountAsync(user))) return;
        if (!Check(await users.UpdateSecurityStampAsync(user))) return;
        await transaction.CommitAsync();
        logger.LogInformation("Administrator {Admin} updated account {User}: role {Role}, disabled {Disabled}.",
            User.Identity?.Name, user.UserName, Role, Disabled);
    });

    public Task<IActionResult> OnPostResetPasswordAsync() => ChangeAsync(async () =>
    {
        if (!ValidatePassword()) return;
        var user = await users.FindByIdAsync(UserId);
        if (user is null)
        {
            ModelState.AddModelError("", "Account not found.");
            return;
        }
        var token = await users.GeneratePasswordResetTokenAsync(user);
        if (!Check(await users.ResetPasswordAsync(user, token, NewPassword))) return;
        logger.LogInformation("Administrator {Admin} reset the password for {User}.", User.Identity?.Name, user.UserName);
    });

    private bool ValidatePassword()
    {
        if (NewPassword.Length is < 12 or > 1024)
        {
            ModelState.AddModelError("", "Passwords must contain between 12 and 1024 characters.");
            return false;
        }
        return true;
    }

    private bool Check(IdentityResult result)
    {
        foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        return result.Succeeded;
    }

    private async Task<IActionResult> ChangeAsync(Func<Task> action)
    {
        await AccountChanges.WaitAsync(HttpContext.RequestAborted);
        try
        {
            var actor = await users.GetUserAsync(User);
            if (actor is not null)
            {
                await db.Entry(actor).ReloadAsync(HttpContext.RequestAborted);
            }
            if (actor is null || await users.IsLockedOutAsync(actor)
                || !await users.IsInRoleAsync(actor, AccountRoles.Administrator)
                || actor.SecurityStamp != User.FindFirst(new IdentityOptions().ClaimsIdentity.SecurityStampClaimType)?.Value)
            {
                return Forbid();
            }
            if (ModelState.IsValid) await action();
        }
        finally
        {
            AccountChanges.Release();
        }
        if (!ModelState.IsValid)
        {
            logger.LogWarning("Account change by {Admin} was rejected.", User.Identity?.Name);
            await OnGetAsync();
            return Page();
        }
        TempData["Status"] = "Account updated.";
        return RedirectToPage();
    }
}
