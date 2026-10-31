using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Verifies an access token another OpenID Connect issuer signed, as an ASP.NET Core bearer handler pointed at that
/// issuer's authority verifies one: signed with a key the issuer's discovery document publishes, by the issuer that
/// document names, unexpired (the handler's five minutes' skew), typed <c>at+jwt</c>, and no audience checked.
/// </summary>
/// <remarks>
/// <para>
/// For a service that must accept a token it didn't issue and its own authentication doesn't trust, such as one
/// another Bitwarden server's identity provider signed. The discovery
/// document and keys are read on first use and kept, refreshed as <see cref="ConfigurationManager{T}"/> refreshes them.
/// A token naming a key not yet seen asks for them to be read again, as a bearer handler does, and is tried once more
/// against what is then current; the manager reads in the background, so an issuer's new key is followed within
/// moments. Requires HTTPS for an <c>https</c> authority only.
/// </para>
/// <para>
/// The token is never logged or kept. The caller reads what it needs from the result's claims.
/// </para>
/// </remarks>
public sealed class DiscoveredIssuerTokens
{
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configuration;
    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false };

    /// <summary>
    /// Over the issuer at <paramref name="authority"/>, its discovery document read with <paramref name="http"/>.
    /// </summary>
    /// <param name="authority">The issuer's address; its discovery document is at <c>/.well-known/openid-configuration</c>.</param>
    /// <param name="http">Reads the document and keys; the caller owns it.</param>
    public DiscoveredIssuerTokens(Uri authority, HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(http);

        var discovery = new Uri(authority.AbsoluteUri.TrimEnd('/') + "/.well-known/openid-configuration");
        _configuration = new ConfigurationManager<OpenIdConnectConfiguration>(
            discovery.AbsoluteUri,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(http) { RequireHttps = discovery.Scheme == Uri.UriSchemeHttps });
    }

    /// <summary>
    /// Verifies the token.
    /// </summary>
    /// <param name="token">The bearer token, as the client sent it.</param>
    /// <param name="cancellationToken">Cancels the read of the issuer's keys.</param>
    /// <returns>The handler's result: <see cref="TokenValidationResult.IsValid"/>, and the token and its claims when valid.</returns>
    /// <exception cref="InvalidOperationException">The issuer's discovery document or keys can't be read.</exception>
    public async Task<TokenValidationResult> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var result = await ValidateAsync(token, await _configuration.GetConfigurationAsync(cancellationToken));

        // The issuer may have rolled its key since it was read: ask for the keys again, and try once more.
        if (result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            _configuration.RequestRefresh();
            result = await ValidateAsync(token, await _configuration.GetConfigurationAsync(cancellationToken));
        }

        return result;
    }

    private Task<TokenValidationResult> ValidateAsync(string token, OpenIdConnectConfiguration issuer) =>
        _handler.ValidateTokenAsync(token, Parameters(issuer));

    /// <summary>
    /// A bearer handler's: the issuer and keys the document publishes, no audience, an access token's type.
    /// </summary>
    internal static TokenValidationParameters Parameters(OpenIdConnectConfiguration issuer) => new()
    {
        ValidIssuer = issuer.Issuer,
        IssuerSigningKeys = issuer.SigningKeys,
        ValidateAudience = false,
        ValidTypes = ["at+jwt"],
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
    };
}
