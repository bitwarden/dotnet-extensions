using Bitwarden.Server.Sdk.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Verifies the context token a call from another service carries (<see cref="BitwardenHeaders.Delegation"/>).
/// </summary>
/// <remarks>
/// <para>
/// With the keys and issuer this service's bearer handler trusts: Identity's, from its published
/// configuration. The token must be of type <see cref="CallerContextToken.Type"/>, unexpired, addressed
/// to this service by its own name, and issued to the caller presenting the service token
/// (<c>client_id</c>), so one taken from a call is useless at any other callee or from any other caller.
/// </para>
/// <para>
/// The plain headers stay the readable copy: one that is present must say what the token says. A call with no
/// context token and none of the headers asserts nothing, and its context is empty: it gains nothing an asserted empty
/// context, which any service may have signed, wouldn't give it. This is how a caller outside the network reaches a
/// service, through a public entry point the host has strip every caller-context header.
/// </para>
/// </remarks>
internal static class CallerDelegation
{
    /// <summary>
    /// Why a call was refused.
    /// </summary>
    internal sealed record Refused(int Status, string Header, string Detail);

    /// <summary>
    /// The call's verified context, or why it was refused; neither for a call carrying no caller context at all,
    /// whose context is empty.
    /// </summary>
    internal static async Task<(VerifiedContext? Context, Refused? Refusal)> VerifyAsync(HttpContext http)
    {
        var headers = http.Request.Headers;

        foreach (var name in BitwardenHeaders.All)
        {
            if (headers[name].Count > 1)
            {
                return (null, new(StatusCodes.Status400BadRequest, name, Malformed(name, "It is sent more than once.")));
            }
        }

        if (headers[BitwardenHeaders.Delegation].FirstOrDefault() is not { Length: > 0 } encoded)
        {
            // A call asserting nothing needs nothing signed: its context is empty, as an asserted empty context's
            // would be, which any service may have. One asserting something unsigned is refused.
            return BitwardenHeaders.All.Any(headers.ContainsKey)
                ? (null, Unsigned("The call carries caller context but no context token. A service's calls carry the context Identity signed for them."))
                : (null, null);
        }

        var validated = await ValidateAsync(http, encoded);

        if (!validated.IsValid || validated.SecurityToken is not JsonWebToken token)
        {
            return (null, Unsigned("The context token is not one this service accepts: forged, expired, or issued for another service."));
        }

        if (!token.TryGetPayloadValue<string>(CallerContextToken.ClientClaim, out var client)
            || !string.Equals(client, http.User.FindFirst(InternalCaller.ClientClaim)?.Value, StringComparison.Ordinal))
        {
            return (null, Unsigned("The context token was issued to another caller."));
        }

        if (!token.TryGetPayloadValue<string>(CallerContextToken.ModeClaim, out var mode)
            || mode is not (CallerContextToken.Delegated or CallerContextToken.Asserted))
        {
            return (null, Unsigned("The context token says neither how it was delegated nor that it was asserted."));
        }

        if (!Owner(token, CallerContextToken.OrganizationClaim, out var organization)
            || !Owner(token, CallerContextToken.UserClaim, out var user)
            || !Owner(token, CallerContextToken.ProviderClaim, out var provider))
        {
            return (null, Unsigned("The context token names an owner that is not an identifier."));
        }

        token.TryGetPayloadValue<string>(CallerContextToken.ContextClaim, out var written);

        if (!CallerContextHeader.TryParse(written, out var context, out var problem))
        {
            return (null, new(StatusCodes.Status400BadRequest, BitwardenHeaders.Context, Malformed(BitwardenHeaders.Context, problem)));
        }

        var disagreeing = Disagrees(headers, BitwardenHeaders.Organization, organization)
            ?? Disagrees(headers, BitwardenHeaders.User, user)
            ?? Disagrees(headers, BitwardenHeaders.Provider, provider)
            ?? (headers[BitwardenHeaders.Context].FirstOrDefault() is { } plain && !string.Equals(plain, written, StringComparison.Ordinal)
                ? BitwardenHeaders.Context
                : null);

        if (disagreeing is not null)
        {
            return (null, new(
                StatusCodes.Status400BadRequest,
                disagreeing,
                $"The '{disagreeing}' header says something other than the call's context token, which is what this service believes."));
        }

        return (new VerifiedContext(organization, user, provider, context, encoded, mode, Hops(token)), null);
    }

    /// <summary>
    /// The length of a verified token's <c>act</c> chain: one per service the call passed through.
    /// </summary>
    private static int Hops(JsonWebToken token)
    {
        if (!token.TryGetPayloadValue<System.Text.Json.JsonElement>(CallerContextToken.ActorClaim, out var actor))
        {
            return 1;
        }

        var hops = 0;

        while (actor.ValueKind == System.Text.Json.JsonValueKind.Object && hops < 64)
        {
            hops++;

            if (!actor.TryGetProperty(CallerContextToken.ActorClaim, out actor))
            {
                break;
            }
        }

        return Math.Max(1, hops);
    }

    private static Refused Unsigned(string detail) =>
        new(StatusCodes.Status401Unauthorized, BitwardenHeaders.Delegation, detail);

    private static string Malformed(string header, string? problem) =>
        $"The '{header}' header is not one this service accepts. {problem}";

    /// <summary>
    /// The header's name when it is present and names something other than the token, else null.
    /// </summary>
    private static string? Disagrees(IHeaderDictionary headers, string name, Guid? signed) =>
        headers[name].FirstOrDefault() is { } plain
        && !(Guid.TryParse(plain, out var value) && value == signed)
            ? name
            : null;

    /// <summary>
    /// An owner claim: absent, or an identifier.
    /// </summary>
    private static bool Owner(JsonWebToken token, string claim, out Guid? owner)
    {
        owner = null;

        if (!token.TryGetPayloadValue<string>(claim, out var value) || value is null)
        {
            return true;
        }

        if (Guid.TryParse(value, out var parsed) && parsed != Guid.Empty)
        {
            owner = parsed;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Validates the token as the bearer handler validates an access token, with the same keys and
    /// issuer, but for a context token addressed to this service.
    /// </summary>
    private static async Task<TokenValidationResult> ValidateAsync(HttpContext http, string encoded)
    {
        var options = http.RequestServices.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var audience = http.RequestServices.GetRequiredService<IServiceIdentity>().Audience;

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(
            encoded, await ParametersAsync(options, audience, http.RequestAborted));

        // A key this service hasn't seen yet: Identity may have rotated. Once, as the bearer handler does.
        if (!result.IsValid
            && result.Exception is SecurityTokenSignatureKeyNotFoundException
            && options.ConfigurationManager is { } manager)
        {
            manager.RequestRefresh();
            result = await new JsonWebTokenHandler().ValidateTokenAsync(
                encoded, await ParametersAsync(options, audience, http.RequestAborted));
        }

        return result;
    }

    private static async Task<TokenValidationParameters> ParametersAsync(
        JwtBearerOptions options,
        string audience,
        CancellationToken cancellationToken)
    {
        var parameters = options.TokenValidationParameters.Clone();
        var configuration = options.Configuration;

        if (configuration is null && options.ConfigurationManager is { } manager)
        {
            configuration = await manager.GetConfigurationAsync(cancellationToken);
        }

        if (configuration is not null)
        {
            parameters.IssuerSigningKeys = [.. parameters.IssuerSigningKeys ?? [], .. configuration.SigningKeys];

            // The published issuer joins those the options name, as the bearer handler adds it. Binding the scheme
            // from configuration, as a deployed service does, leaves ValidIssuers an empty list rather than null.
            if (configuration.Issuer is { Length: > 0 } issuer)
            {
                parameters.ValidIssuers = [.. parameters.ValidIssuers ?? [], issuer];
            }
        }

        parameters.ValidTypes = [CallerContextToken.Type];
        parameters.ValidAudience = audience;
        parameters.ValidAudiences = null;
        parameters.ValidateAudience = true;
        parameters.ValidateIssuer = true;
        parameters.ValidateLifetime = true;
        parameters.RequireExpirationTime = true;
        parameters.RequireSignedTokens = true;
        return parameters;
    }
}
