using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Serial2CNC.Web.Pages.Account;

public sealed class LogoutModel(SignInManager<IdentityUser> signIn, ILogger<LogoutModel> logger) : PageModel
{
    public async Task<IActionResult> OnPostAsync()
    {
        var user = await signIn.UserManager.GetUserAsync(User);
        if (user is not null)
        {
            // Invalidate existing Blazor circuits as well as the browser cookie.
            Security.AccountSetup.Ensure(await signIn.UserManager.UpdateSecurityStampAsync(user));
        }
        await signIn.SignOutAsync();
        logger.LogInformation("User {User} signed out.", User.Identity?.Name);
        return RedirectToPage("Login");
    }
}
