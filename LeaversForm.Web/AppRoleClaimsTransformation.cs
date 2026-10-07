using System.Security.Claims;
using LeaversForm.Data.Enums;
using LeaversForm.Services;
using Microsoft.AspNetCore.Authentication;

namespace LeaversForm.Web;

public sealed class AppRoleClaimsTransformation(IAppUserService appUsers) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return principal;

        var identity = principal.Identity as ClaimsIdentity;
        if (identity is null)
            return principal;

        var email = LeaversRoles.GetEmail(principal);
        var role = await appUsers.GetRoleByEmailAsync(email);
        var roleName = role switch
        {
            AppRole.SuperUser => LeaversRoles.SuperUser,
            AppRole.Admin => LeaversRoles.Admin,
            _ => LeaversRoles.User
        };

        foreach (var claim in identity.FindAll(identity.RoleClaimType).ToList())
            identity.RemoveClaim(claim);

        identity.AddClaim(new Claim(identity.RoleClaimType, roleName));

        if (!string.IsNullOrWhiteSpace(email)
            && identity.FindFirst(ClaimTypes.Email) is null
            && identity.FindFirst("preferred_username") is null)
        {
            identity.AddClaim(new Claim(ClaimTypes.Email, email));
        }

        return principal;
    }
}
