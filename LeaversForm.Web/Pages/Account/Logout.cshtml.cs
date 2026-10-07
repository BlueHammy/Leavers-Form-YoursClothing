using LeaversForm.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LeaversForm.Web.Pages.Account;

[Authorize]
public class LogoutModel(IWebHostEnvironment environment, IConfiguration configuration) : PageModel
{
    public async Task<IActionResult> OnPostAsync()
    {
        if (AuthMode.UseDevelopmentBypass(environment, configuration))
        {
            await HttpContext.SignOutAsync("Development");
            return RedirectToPage("/Account/SignedOut");
        }

        return SignOut(
            new AuthenticationProperties { RedirectUri = Url.Page("/Account/SignedOut") },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }
}
