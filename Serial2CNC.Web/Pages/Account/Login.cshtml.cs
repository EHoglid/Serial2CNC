using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Serial2CNC.Web.Security;

namespace Serial2CNC.Web.Pages.Account;

[AllowAnonymous]
public sealed class LoginModel(SignInManager<IdentityUser> signIn, ILogger<LoginModel> logger) : PageModel
{
    [BindProperty, Required, StringLength(256)]
    public string UserName { get; set; } = "";
    [BindProperty, Required, StringLength(1024)]
    public string Password { get; set; } = "";
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }
        var user = await signIn.UserManager.FindByNameAsync(UserName);
        var result = user is null ? Microsoft.AspNetCore.Identity.SignInResult.Failed
            : await signIn.CheckPasswordSignInAsync(user, Password, lockoutOnFailure: true);
        if (!result.Succeeded || user is null)
        {
            logger.LogWarning("Failed login from {Address}.", HttpContext.Connection.RemoteIpAddress);
            ModelState.AddModelError("", "Sign-in failed. Check your credentials or try again later.");
            return Page();
        }
        await signIn.SignInWithClaimsAsync(user, isPersistent: false,
            [new Claim(AccountRoles.SessionExpiry, DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeSeconds().ToString())]);
        logger.LogInformation("User {User} signed in.", user.UserName);
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
    }
}
