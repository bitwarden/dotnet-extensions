using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Accepts only tokens that name a person, in a product service.
/// </summary>
/// <remarks>
/// <para>
/// In such a service the acting user is the token's <c>sub</c>, so a token without one (a
/// service's own credential, or anything else issued to no person) has nobody to act for. It is
/// failed at authentication rather than at authorization, so the caller is told <c>401</c>: the
/// credential is not one this service accepts, which is a different answer from "you may not".
/// </para>
/// <para>
/// A <c>sub</c> that is not an identifier is failed the same way. The platform's identifiers are
/// GUIDs, and a subject that cannot be one would otherwise authenticate a caller with no user.
/// </para>
/// <para>
/// The exceptions are an operation marked <see cref="AcceptsSendCredentialAttribute"/>, which takes a
/// token minted for one Send's link, and an operation marked <see cref="AcceptsOrganizationCredentialAttribute"/>,
/// which also accepts a token issued to an organization: an <c>organization</c> claim and no
/// <c>sub</c> at all. Routing has chosen the endpoint before authentication runs, so the
/// declaration is read from it; any other operation refuses that token as it always did.
/// </para>
/// <para>
/// Applied after every other configuration of the scheme, including a test host's, and wrapping
/// whatever <see cref="JwtBearerEvents.OnTokenValidated"/> was already there rather than
/// replacing it.
/// </para>
/// </remarks>
internal sealed class PersonSessionTokens : IPostConfigureOptions<JwtBearerOptions>
{
    /// <summary>
    /// Why a token was failed, for the log. Never shown to the caller.
    /// </summary>
    internal const string Failure = "The token names no person, and this is a product service, called by people.";

    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        options.Events ??= new JwtBearerEvents();
        var previous = options.Events.OnTokenValidated;

        options.Events.OnTokenValidated = async validated =>
        {
            await previous(validated);

            // Something earlier already decided; its answer stands.
            if (validated.Result is not null)
            {
                return;
            }

            var subject = validated.Principal?.FindFirst(Context.SubjectClaim)?.Value;

            if (Guid.TryParse(subject, out _))
            {
                return;
            }

            // An organization credential (a token for an organization's API key: an
            // organization, no sub), to an operation that declares it accepts one. Every other
            // operation refuses it here.
            if (validated.Principal is { } principal
                && OrganizationCredential.Of(principal) is not null
                && OrganizationCredential.DeclaredBy(validated.HttpContext) is not null)
            {
                return;
            }

            // A Send credential (a token minted for one Send's link: a send_id, no person), to an
            // operation that declares it accepts one.
            if (validated.Principal is { } recipient
                && SendCredential.SendId(recipient) is not null
                && SendCredential.DeclaredBy(validated.HttpContext))
            {
                return;
            }

            validated.Fail(Failure);
        };
    }
}
