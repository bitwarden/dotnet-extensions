using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;

namespace Bitwarden.Core;

public sealed class IdentityAuthenticatingOptions
{
    public Func<BitwardenIdentityClient, Task<AuthenticationPayload>> Authenticate { get; set; } = null!;
}

internal sealed class IdentityAuthenticatingHandler : DelegatingHandler
{


    public IdentityAuthenticatingHandler(BitwardenIdentityClient identityClient, IdentityAuthenticatingOptions options)
    {
        // TODO: Create background task to refresh the token constantly
        _identityClient = identityClient;
        _options = options;
    }
    internal static HttpRequestOptionsKey<bool> SkipAuthentication { get; } = new HttpRequestOptionsKey<bool>("BitwardenSkipAuthentication");

    private readonly BitwardenIdentityClient _identityClient;
    private readonly IdentityAuthenticatingOptions _options;
    private AuthenticationPayload? _payload;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Options.TryGetValue(SkipAuthentication, out var shouldSkipAuthentication) && shouldSkipAuthentication)
        {
            return base.SendAsync(request, cancellationToken);
        }

        // TODO: Are we likely authenticated?
        if (IsLikelyAuthenticated())
        {
            // TODO: We should handle 401 and try one more time with a new token.
            AddAuthorizationHeader(request, _payload);
            return base.SendAsync(request, cancellationToken);
        }

        return SendWithAuthenticationAsync(request, cancellationToken);
    }

    [MemberNotNullWhen(true, nameof(_payload))]
    private bool IsLikelyAuthenticated()
    {
        if (_payload is null)
        {
            return false;
        }
        var now = DateTime.UtcNow;
        // Take a little off the backend to force it to get a new token before it expires
        return _payload.AccessToken.ValidFrom < now && now < _payload.AccessToken.ValidTo.AddMinutes(-5);
    }

    private async Task<HttpResponseMessage> SendWithAuthenticationAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _payload = _payload is RefreshableAuthenticationPayload refresh
            ? await _identityClient.RefreshLoginAsync(refresh.RefreshToken)
            : await _options.Authenticate(_identityClient);

        AddAuthorizationHeader(request, _payload);
        return await base.SendAsync(request, cancellationToken);
    }

    private static void AddAuthorizationHeader(HttpRequestMessage request, AuthenticationPayload payload)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue(payload.TokenType, payload.AccessToken.ToString());
    }
}
