using Bitwarden.Server.Sdk.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Accepts only tokens addressed to this service by its own name: a person's token exchanged for a
/// product service, or another service's token issued for this callee (<see cref="TokenAudience"/>).
/// </summary>
/// <remarks>
/// <para>
/// On for every service the hosting model builds, and not a setting. A service that could forget
/// to turn it on would accept, at a product service, a person's token exchanged for another
/// product, and at a domain service a token another service obtained for a different callee: the
/// leaks P06 asks us to bound.
/// </para>
/// <para>
/// The expected audience is <see cref="IServiceIdentity.Audience"/>: the service names itself once,
/// in its constructor. A token naming
/// several audiences is accepted when one of them is this service's, as RFC 7519 reads <c>aud</c>.
/// </para>
/// <para>
/// A service that must also accept another audience, at one surface it hosts, adds it to
/// <c>TokenValidationParameters.ValidAudiences</c> from <c>ConfigureServices</c>; this keeps what it
/// finds and adds its own. Such a service is then responsible for keeping the extra audience to
/// that surface. Turning validation off takes a post-configuration of the scheme registered after
/// this one: deliberate, and visible in review.
/// </para>
/// <para>
/// Here rather than in <c>Bitwarden.Server.Sdk.Authentication</c>, whose default stays off: that
/// package also authenticates tokens that carry no audience a service could check.
/// </para>
/// </remarks>
internal sealed class ServiceAudience(IServiceIdentity identity) : IPostConfigureOptions<JwtBearerOptions>
{
    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        var parameters = options.TokenValidationParameters;

        parameters.ValidAudiences =
        [
            .. (parameters.ValidAudiences ?? [])
                .Append(parameters.ValidAudience)
                .Append(identity.Audience)
                .OfType<string>()
                .Where(audience => audience.Length > 0)
                .Distinct(StringComparer.Ordinal),
        ];
        parameters.ValidAudience = null;
        parameters.ValidateAudience = true;
    }
}
