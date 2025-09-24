using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Logging;

namespace Bitwarden.Server.Sdk.Authentication;

internal class BitwardenConfigureJwtBearerOptions : IConfigureNamedOptions<JwtBearerOptions>
{
    private readonly IHostEnvironment _hostEnvironment;

    public BitwardenConfigureJwtBearerOptions(IHostEnvironment hostEnvironment)
    {
        _hostEnvironment = hostEnvironment;

        if (_hostEnvironment.IsDevelopment())
        {
            IdentityModelEventSource.ShowPII = true;
        }
    }

    public void Configure(string? schemeName, JwtBearerOptions options)
    {
        if (schemeName != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        // The MapInBoundClaims maps the Jwt claim names into the more microsoft standard of the same claims
        // For example, the claim `"amr"` might exist in the JWT but with MapInboundClaims = true then once
        // you get your hand on the ClaimsPrincipal you'd have to find the claim value using
        // `ClaimTypes.AuthenticationMethod` or `"http://schemas.microsoft.com/claims/authnmethodsreferences"`
        // Keeping the exact same name on the issuing side and the consumption side is less error prone in our
        // opinion.
        options.MapInboundClaims = false;

        // Our identity service does not issue an audience claim for us to validate
        options.TokenValidationParameters.ValidateAudience = false;

        // This is the default AccessTokenJwtType that IdentityServer uses
        options.TokenValidationParameters.ValidTypes = ["at+jwt"];

        // Since we don't map inbound claims our if an email is going to exist
        // it will be as a `"email"` claim.
        options.TokenValidationParameters.NameClaimType = JwtRegisteredClaimNames.Email;
    }

    public void Configure(JwtBearerOptions options)
    {
        // Do nothing for unnamed options
    }
}
