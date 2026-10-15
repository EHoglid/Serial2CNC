using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Serial2CNC.Web.Pages.Account;

public sealed class PasswordModel(UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn,
    ILogger<PasswordModel> logger) : PageModel
{
    [BindProperty, Required, StringLength(1024)] public string CurrentPassword { get; set; } = "";
    [BindProperty, Required, StringLength(1024, MinimumLength = 12)] public string NewPassword { get; set; } = "";
    [BindProperty, Required, Compare(nameof(NewPassword))] public string ConfirmPassword { get; set; } = "";

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        var result = await users.ChangePasswordAsync(user, CurrentPassword, NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
            return Page();
        }
        logger.LogInformation("User {User} changed their password.", user.UserName);
        await signIn.SignOutAsync();
        return RedirectToPage("Login");
    }
}
