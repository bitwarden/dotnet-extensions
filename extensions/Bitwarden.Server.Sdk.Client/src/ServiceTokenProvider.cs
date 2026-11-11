using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bitwarden.Server.Sdk.Context;
using Bitwarden.Server.Sdk.Errors;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Holds the access token a service presents when calling another, and replaces it before it
/// expires.
/// </summary>
/// <remarks>
/// <para>
/// One per configured client, so two clients calling different services with different scopes do
/// not share a token, and each token names the one service it is for
/// (<see cref="BitwardenClientOptions.Audience"/>). The first call fetches one; a timer replaces it thereafter, so no later
/// call pays for the round trip.
/// </para>
/// <para>
/// The timer is the fast path, not the guarantee. It runs on the process's monotonic clock, while
/// the authorization server expires the token by the wall clock, and a timer can fire late or not
/// at all (a container paused with its VM, a timer that stalled). So a token is held only until
/// its renewal time by the wall clock: a call after that fetches a new one itself, rather than
/// sending one the callee refuses.
/// </para>
/// <para>
/// A failure to obtain a token is raised, not swallowed. Sending the call anyway produces a 401
/// that reads as an authorization problem at the callee, which is the wrong place to start
/// looking for a credential that was never issued.
/// </para>
/// </remarks>
public sealed class ServiceTokenProvider : IServiceTokenProvider, IDisposable
{
    private readonly IHttpClientFactory _clients;
    private readonly BitwardenClientOptions _options;
    private readonly ILogger<ServiceTokenProvider> _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // The token and its renewal time, replaced together so a reader never pairs one token with another's time.
    private volatile HeldToken? _held;
    private Timer? _refresh;
    private bool _disposed;

    /// <summary>
    /// Signs this service's client assertions, when it has a <see cref="BitwardenClientOptions.CallerKey"/>.
    /// </summary>
    private readonly ClientAssertion? _assertion;

    /// <summary>
    /// Identity's issuer, read from its discovery document once and kept: an assertion's audience.
    /// </summary>
    private string? _issuer;

    /// <param name="clients">Used to obtain a client for the token endpoint itself.</param>
    /// <param name="options">The credential and where to present it.</param>
    /// <param name="logger">Records renewals and failures.</param>
    /// <param name="time">The wall clock a token's renewal time is kept by; the system's when none is given.</param>
    public ServiceTokenProvider(
        IHttpClientFactory clients,
        BitwardenClientOptions options,
        ILogger<ServiceTokenProvider> logger,
        TimeProvider? time = null)
    {
        _clients = clients;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _assertion = string.IsNullOrWhiteSpace(options.CallerKey) ? null : ClientAssertion.FromPrivateKey(options.CallerKey);
    }

    /// <summary>
    /// The name of the <see cref="IHttpClientFactory"/> client used for token requests.
    /// </summary>
    public const string TokenClientName = "bitwarden-service-token";

    /// <summary>
    /// How long one request to the token endpoint may take: the bound on a renewal no call waits for, in place of
    /// .NET's default 100 seconds. A call waiting for a token is bounded too by its client's
    /// <see cref="BitwardenClientOptions.Timeout"/>, whichever passes first; the two are the same by default, so a call
    /// waits for Identity as long as for the callee.
    /// </summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The service named when obtaining a token is what failed (<see cref="ServiceUnavailableException.Service"/>), in
    /// place of the callee, which was never asked.
    /// </summary>
    public const string Issuer = "identity";

    /// <inheritdoc />
    /// <exception cref="ServiceUnavailableException">Identity is unavailable (<see cref="Issuer"/>).</exception>
    /// <exception cref="ServiceCallException">Identity refused this service's credential.</exception>
    public async Task<string> GetAsync(CancellationToken cancellationToken = default)
    {
        if (Current() is { } current)
        {
            return current;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            return Current() ?? await AcquireAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The token held, until its renewal time by the wall clock; then none.
    /// </summary>
    private string? Current() => _held is { } held && _time.GetUtcNow() < held.RenewAt ? held.Token : null;

    private async Task<string> AcquireAsync(CancellationToken cancellationToken)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "client_credentials"),
            new("scope", string.Join(' ', _options.Scopes)),
            // The one service this token is for (RFC 8693's parameter, which the authorization
            // server reads for every grant): a token per callee.
            new("audience", _options.Audience),
        };
        await AuthenticateAsync(fields, cancellationToken);

        var issued = await RequestAsync(
            fields,
            "a token",
            "The credential, the scopes or callees it may request, or the identity address is wrong.",
            cancellationToken);

        _held = new HeldToken(issued.AccessToken, _time.GetUtcNow() + Renewal(issued.ExpiresIn));
        Schedule(issued.ExpiresIn);

        _logger.LogInformation(
            "Obtained a token for internal.{Caller}, valid for {Seconds}s.",
            _options.CallerName,
            issued.ExpiresIn);

        return issued.AccessToken;
    }

    /// <summary>
    /// Context tokens held, by the subject, callee and context they were issued for, until their
    /// renewal time. Bounded: past <see cref="MaximumContextTokens"/> the expired go, and if that is
    /// not enough, all of them.
    /// </summary>
    private readonly ConcurrentDictionary<string, (string Token, DateTimeOffset RenewAt)> _contextTokens = new(StringComparer.Ordinal);

    /// <summary>
    /// How many context tokens one client holds at most.
    /// </summary>
    internal const int MaximumContextTokens = 10_000;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// RFC 8693 token exchange at Identity, as this service (<c>internal.{name}</c>, with its client assertion or its secret),
    /// for the callee this provider serves. The subject is what the caller was handed: a person's
    /// token at a product service, the context token a domain service was called with; or, for a
    /// call that is the service's own, its own service token for the callee. Identity checks the
    /// subject is addressed to this service and agrees with what is asserted, and signs the context
    /// for this callee and this caller alone.
    /// </para>
    /// <para>
    /// Kept until <see cref="BitwardenClientOptions.RefreshAt"/> of its life for the same subject,
    /// callee and context, so a person's requests cost one exchange per callee every few minutes,
    /// not one per call. Keyed by a digest, so no token is held as a key. A failure is raised, as
    /// for the service token: a call sent without one is refused by the callee.
    /// </para>
    /// </remarks>
    /// <exception cref="ServiceUnavailableException">Identity is unavailable (<see cref="Issuer"/>).</exception>
    /// <exception cref="ServiceCallException">Identity refused the exchange.</exception>
    public async Task<string?> GetContextTokenAsync(
        ContextAssertion assertion,
        string? subjectToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assertion);

        var key = ContextKey(assertion, subjectToken);

        if (_contextTokens.TryGetValue(key, out var held) && _time.GetUtcNow() < held.RenewAt)
        {
            return held.Token;
        }

        var subject = subjectToken ?? await GetAsync(cancellationToken);
        var fields = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "urn:ietf:params:oauth:grant-type:token-exchange"),
            new("audience", _options.Audience),
            new("requested_token_type", ContextToken.TokenType),
            new("subject_token", subject),
            new("subject_token_type", IsContextToken(subject) ? ContextToken.TokenType : ContextToken.AccessTokenType),
            new(ContextToken.ContextClaim, assertion.Context),
        };

        await AuthenticateAsync(fields, cancellationToken);

        if (assertion.Organization is { } organization)
        {
            fields.Add(new(ContextToken.OrganizationClaim, organization.ToString()));
        }

        if (assertion.User is { } user)
        {
            fields.Add(new(ContextToken.UserClaim, user.ToString()));
        }

        if (assertion.Provider is { } provider)
        {
            fields.Add(new(ContextToken.ProviderClaim, provider.ToString()));
        }

        var issued = await RequestAsync(
            fields,
            "a context token",
            "The subject or the asserted context was refused, or the credential is wrong.",
            cancellationToken);

        if (_contextTokens.Count >= MaximumContextTokens)
        {
            var now = _time.GetUtcNow();

            foreach (var expired in _contextTokens.Where(entry => entry.Value.RenewAt <= now).Select(entry => entry.Key).ToList())
            {
                _contextTokens.TryRemove(expired, out _);
            }

            if (_contextTokens.Count >= MaximumContextTokens)
            {
                _contextTokens.Clear();
            }
        }

        _contextTokens[key] = (issued.AccessToken, _time.GetUtcNow() + Renewal(issued.ExpiresIn));
        return issued.AccessToken;
    }

    /// <summary>
    /// Adds this service's client authentication to a token request: a <see cref="ClientAssertion"/> signed with its
    /// <see cref="BitwardenClientOptions.CallerKey"/> when it has one (RFC 7523's <c>private_key_jwt</c>), addressed to
    /// Identity's issuer; otherwise its secret (<c>client_secret_post</c>). Never both: a request authenticated twice is
    /// refused.
    /// </summary>
    /// <exception cref="ServiceUnavailableException">Identity's issuer couldn't be read.</exception>
    private async Task AuthenticateAsync(List<KeyValuePair<string, string>> fields, CancellationToken cancellationToken)
    {
        var clientId = $"internal.{_options.CallerName}";
        fields.Add(new("client_id", clientId));

        if (_assertion is null)
        {
            fields.Add(new("client_secret", _options.CallerSecret));
            return;
        }

        var issuer = _issuer ??= await IssuerAsync(cancellationToken);
        fields.Add(new("client_assertion_type", ClientAssertion.Type));
        fields.Add(new("client_assertion", _assertion.Create(clientId, issuer, _time.GetUtcNow())));
    }

    /// <summary>
    /// Identity's issuer, from its discovery document at <see cref="BitwardenClientOptions.IdentityUri"/>: the name it
    /// signs as, which may differ from the address this service reaches it at (its public address, say).
    /// </summary>
    private async Task<string> IssuerAsync(CancellationToken cancellationToken)
    {
        var http = _clients.CreateClient(TokenClientName);

        try
        {
            using var response = await http.GetAsync(
                $"{_options.IdentityUri.TrimEnd('/')}/.well-known/openid-configuration", cancellationToken);
            response.EnsureSuccessStatusCode();

            var document = await response.Content.ReadFromJsonAsync(ServiceClientJsonContext.Default.IssuerDocument, cancellationToken);

            return document?.Issuer is { Length: > 0 } issuer
                ? issuer
                : throw new ServiceUnavailableException(Issuer, "published no issuer in its discovery document");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new ServiceUnavailableException(
                Issuer, $"could not be asked for its issuer for calls to {_options.Audience}", exception);
        }
    }

    /// <summary>
    /// One request to Identity's token endpoint, with its failures said as what they are: Identity unavailable, or
    /// this service's own credential refused. Never as the callee's answer, which was never asked for.
    /// </summary>
    /// <param name="fields">The form.</param>
    /// <param name="what">What is asked for, completing "could not obtain …".</param>
    /// <param name="refused">Why Identity might refuse it, for the one who reads the log.</param>
    /// <param name="cancellationToken">The call's own, bounded by its client's deadline.</param>
    /// <exception cref="ServiceUnavailableException">
    /// Identity could not be reached, did not answer within <see cref="RequestTimeout"/>, or answered <c>502</c>,
    /// <c>503</c> or <c>504</c>.
    /// </exception>
    /// <exception cref="ServiceCallException">
    /// Identity refused, or answered anything else unsuccessful. Its <see cref="ServiceCallException.Status"/> is
    /// <c>500</c>, this service's own failure, so no caller reads Identity's status as the callee's (a <c>404</c> as
    /// "not found"); Identity's status is in the message and the inner <see cref="HttpRequestException.StatusCode"/>.
    /// </exception>
    private async Task<IssuedToken> RequestAsync(
        IEnumerable<KeyValuePair<string, string>> fields,
        string what,
        string refused,
        CancellationToken cancellationToken)
    {
        var http = _clients.CreateClient(TokenClientName);

        try
        {
            using var response = await http.PostAsync(
                $"{_options.IdentityUri.TrimEnd('/')}/connect/token",
                new FormUrlEncodedContent(fields),
                cancellationToken);

            if (response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
            {
                throw new ServiceUnavailableException(
                    Issuer,
                    $"answered {(int)response.StatusCode} when asked for {what} for calls to {_options.Audience}");
            }

            if (!response.IsSuccessStatusCode)
            {
                var message = $"internal.{_options.CallerName} could not obtain {what} for {_options.Audience} from "
                    + $"{_options.IdentityUri}, which answered {(int)response.StatusCode}. {refused}";

                throw new ServiceCallException(
                    HttpStatusCode.InternalServerError,
                    message,
                    null,
                    new HttpRequestException(message, null, response.StatusCode));
            }

            return await response.Content.ReadFromJsonAsync(ServiceClientJsonContext.Default.IssuedToken, cancellationToken)
                ?? throw new ServiceCallException(
                    HttpStatusCode.InternalServerError,
                    $"The token endpoint returned nothing when asked for {what}.");
        }
        catch (HttpRequestException exception)
        {
            throw new ServiceUnavailableException(
                Issuer,
                $"could not be reached for {what} for calls to {_options.Audience}",
                exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Cancelled, but not by the token passed in (the call's deadline, or the caller giving up): the token
            // client's own bound (RequestTimeout). However the client words it, a timeout inside the response's body
            // included, it is Identity not answering.
            throw new ServiceUnavailableException(
                Issuer,
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"did not issue {what} for calls to {_options.Audience} within {http.Timeout.TotalSeconds:0.###} seconds"),
                exception);
        }
    }

    /// <summary>
    /// A digest of what a context token is for: the subject (or the service itself), and the context.
    /// </summary>
    private static string ContextKey(ContextAssertion assertion, string? subjectToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{subjectToken ?? "-"}\n{assertion.Organization}\n{assertion.User}\n{assertion.Provider}\n{assertion.Context}")));

    /// <summary>
    /// Whether a JWT is a context token, by its JOSE type; read, never trusted (Identity verifies it).
    /// </summary>
    internal static bool IsContextToken(string token)
    {
        var dot = token.IndexOf('.', StringComparison.Ordinal);

        if (dot <= 0)
        {
            return false;
        }

        try
        {
            var header = token[..dot].Replace('-', '+').Replace('_', '/');
            header = header.PadRight(header.Length + ((4 - (header.Length % 4)) % 4), '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(header));

            return json.RootElement.TryGetProperty("typ", out var type)
                && type.ValueKind == JsonValueKind.String
                && string.Equals(type.GetString(), ContextToken.Type, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return false;
        }
    }

    /// <remarks>
    /// The timer both replaces the token and reschedules itself from the new lifetime, so a
    /// change in what the authorization server issues is followed rather than assumed.
    /// </remarks>
    private void Schedule(int lifetimeSeconds)
    {
        if (_disposed)
        {
            return;
        }

        var due = Renewal(lifetimeSeconds);

        _refresh ??= new Timer(async _ => await RenewAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _refresh.Change(due, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// When, after it is issued, a token is replaced: <see cref="BitwardenClientOptions.RefreshAt"/> of its lifetime.
    /// </summary>
    private TimeSpan Renewal(int lifetimeSeconds) => TimeSpan.FromSeconds(Math.Max(1, lifetimeSeconds * _options.RefreshAt));

    private async Task RenewAsync()
    {
        try
        {
            await _gate.WaitAsync();

            try
            {
                _held = null;
                await AcquireAsync(CancellationToken.None);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception exception)
        {
            // The next call obtains a token itself. A renewal failure is worth reporting but not
            // worth taking the process down for.
            _logger.LogError(exception, "Could not renew the token for internal.{Caller}.", _options.CallerName);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        _refresh?.Dispose();
        _gate.Dispose();
        _assertion?.Dispose();
    }

    private sealed record HeldToken(string Token, DateTimeOffset RenewAt);
}
