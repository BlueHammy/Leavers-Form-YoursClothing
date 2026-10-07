using System.Security.Claims;

using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;

using Microsoft.Extensions.Options;



namespace LeaversForm.Web;



public sealed class DevelopmentAuthenticationHandler(

    IOptionsMonitor<AuthenticationSchemeOptions> options,

    ILoggerFactory logger,

    UrlEncoder encoder)

    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)

{

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()

    {

        var identity = new ClaimsIdentity(

            [

                new Claim(ClaimTypes.Name, "Michael Newton"),

                new Claim(ClaimTypes.Email, "michaelnewton@yoursclothing.co.uk"),

                new Claim("preferred_username", "michaelnewton@yoursclothing.co.uk")

            ],

            Scheme.Name);



        return Task.FromResult(AuthenticateResult.Success(

            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));

    }

}

