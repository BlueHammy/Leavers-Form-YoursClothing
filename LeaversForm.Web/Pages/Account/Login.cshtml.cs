using LeaversForm.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LeaversForm.Web.Pages.Account;

[AllowAnonymous]
public class LoginModel(IWebHostEnvironment environment, IConfiguration configuration) : PageModel
{
    public IActionResult OnGet(string? returnUrl = null, bool reauth = false)
    {
        if (User.Identity?.IsAuthenticated == true)
            return Redirect(returnUrl ?? "/Leavers/Create");

        var redirect = returnUrl ?? "/Leavers/Create";
        if (AuthMode.UseDevelopmentBypass(environment, configuration))
            return Redirect(redirect);

        if (!AuthMode.UseEntraSso(configuration))
            return Content("Azure AD is not configured. Set AzureAd:TenantId, ClientId, and ClientSecret via user secrets. See docs/SEMI-LIVE-TESTING.md.", "text/plain");

        var properties = new AuthenticationProperties { RedirectUri = redirect };
        if (reauth)
            properties.Items["prompt"] = "select_account";

        return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
    }
}
