using Bitwarden.Server.Sdk.Sagas;
namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// What a client needs to reach a service and authenticate to it.
/// </summary>
/// <remarks>
/// Every member has a convention behind it, so a consumer that has deployed things the ordinary
/// way configures nothing. What cannot be guessed is reported at startup rather than on the first
/// call, naming the configuration key that would have supplied it.
/// </remarks>
public sealed class BitwardenClientOptions
{
    /// <summary>
    /// The configuration section a client's options are bound from.
    /// </summary>
    /// <remarks>
    /// The service's own name is appended: <c>Services:Reference</c>.
    /// </remarks>
    public const string Section = "Services";

    /// <summary>
    /// Where the service is.
    /// </summary>
    public string BaseAddress { get; set; } = string.Empty;

    /// <summary>
    /// The name this service authenticates as, forming the <c>internal.{name}</c> client id.
    /// </summary>
    public string CallerName { get; set; } = string.Empty;

    /// <summary>
    /// The audience the service's tokens must name: the callee's registered name. An authorization
    /// server that keeps a registry of callers issues a token for it only to a caller allowed to reach it.
    /// </summary>
    /// <remarks>
    /// By convention the name the client package registers the service under, which is the
    /// service's own name (<c>Services:widget-service</c> calls <c>widget-service</c>).
    /// A token is asked for per callee, so one leaked at one service can't be used at another.
    /// </remarks>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Where tokens are issued.
    /// </summary>
    public string IdentityUri { get; set; } = string.Empty;

    /// <summary>
    /// The secret this service authenticates with, when it has no <see cref="CallerKey"/>.
    /// </summary>
    public string CallerSecret { get; set; } = string.Empty;

    /// <summary>
    /// The private key this service authenticates with instead of <see cref="CallerSecret"/>: PKCS#8, base64, P-256 or
    /// RSA. With one, every token request carries a <see cref="ClientAssertion"/> signed with it (RFC 7523's
    /// <c>private_key_jwt</c>) and never the secret; Identity holds only its public half.
    /// </summary>
    public string CallerKey { get; set; } = string.Empty;

    /// <summary>
    /// The scopes to request.
    /// </summary>
    /// <remarks>
    /// Set by the client package rather than the consumer: which scopes a client needs is a
    /// property of the API it calls, not of who is calling. A consumer that may not hold them is
    /// refused by the authorization server, which is where that decision belongs.
    /// </remarks>
    public IReadOnlyList<string> Scopes { get; set; } = [];

    /// <summary>
    /// Whether <see cref="BaseAddress"/> and <see cref="IdentityUri"/> must be <c>https://</c>: the deployment's
    /// <c>Tls:RequireHttps</c>. A plain address then stops the service at startup.
    /// </summary>
    public bool RequireHttps { get; set; }

    /// <summary>
    /// How long to wait for a call before giving up. <c>Services:{Name}:Timeout</c> where set.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many times a call is tried again when the callee wasn't there: a failed connection, or a <c>502</c>, <c>503</c>
    /// or <c>504</c> from in front of it. Only a <c>GET</c> or <c>HEAD</c>, or a write carrying an idempotency key (one to
    /// an operation the callee's contract marks to accept it); any other write only when its connection never opened.
    /// 0 to 2; within <see cref="Timeout"/>.
    /// </summary>
    /// <remarks>
    /// <c>Services:{Name}:Retries</c> where set.
    /// </remarks>
    public int Retries { get; set; } = 2;

    /// <summary>
    /// The callee's write-method operations that only read although their address doesn't say so
    /// (<see cref="ReadingRoutes"/>), or whose only write is the call's own verification (a one-time code spent,
    /// a password rehashed), each as the end of its path, a <c>*</c> standing for one segment, such as
    /// <c>users/*/actions/verify-password</c>. A call to one is not counted as a write of the operation making it
    /// (<see cref="ServiceWrites"/>). Set by the client's own registration, beside
    /// <see cref="SendsWritesAttribute"/> on the methods that call them.
    /// </summary>
    public IList<string> ReadingPaths { get; } = [];

    /// <summary>
    /// The wait before the first retry; the next is twice it, each with jitter (0.5 to 1.5 times).
    /// <c>Services:{Name}:RetryDelay</c> where set.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Attempts in a row that find the callee not there (or not answering within <see cref="Timeout"/>) before calls to
    /// it are refused at once, for <see cref="BreakFor"/>, with the same <c>ServiceUnavailableException</c>. 0 never
    /// refuses.
    /// </summary>
    public int BreakAfter { get; set; } = 5;

    /// <summary>
    /// How long an open circuit refuses calls before one is let through to see whether the callee is back.
    /// </summary>
    public TimeSpan BreakFor { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How much of a token's life to use before replacing it.
    /// </summary>
    /// <remarks>
    /// Below 1 so a token is replaced while it is still valid. Refreshing exactly at expiry would
    /// mean every renewal races the calls it is meant to serve.
    /// </remarks>
    public double RefreshAt { get; set; } = 0.8;
}
