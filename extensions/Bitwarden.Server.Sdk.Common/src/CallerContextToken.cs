namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// The signed form of a call's caller context: a short-lived JWT obtained by RFC 8693 token
/// exchange, which a callee believes in place of the plain <see cref="BitwardenHeaders"/>.
/// </summary>
/// <remarks>
/// <para>
/// The calling service exchanges a subject token for one per callee and context: the person's token
/// it validated, the context token it was itself called with, or its own service token when nobody
/// but the service is behind the call. The issuer checks that what the call asserts agrees with what
/// the subject proves, and signs the context for that callee and caller alone.
/// </para>
/// <para>
/// It carries identifiers only, as <see cref="BitwardenHeaders.Context"/> does. Never a key, an
/// encrypted value or anything about a person beyond who they are.
/// </para>
/// </remarks>
public static class CallerContextToken
{
    /// <summary>
    /// The token's JOSE type. Never <c>at+jwt</c>, so no bearer handler takes it as an access token.
    /// </summary>
    public const string Type = "bitwarden-context+jwt";

    /// <summary>
    /// The <c>requested_token_type</c> of a token request, and the <c>issued_token_type</c> of its answer.
    /// </summary>
    public const string TokenType = "urn:bitwarden:params:oauth:token-type:caller-context";

    /// <summary>
    /// RFC 8693's type for an access token presented as the subject.
    /// </summary>
    public const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    /// <summary>
    /// The organization the call concerns, as <see cref="BitwardenHeaders.Organization"/> names it.
    /// </summary>
    public const string OrganizationClaim = "context_organization";

    /// <summary>
    /// The user the call is made on behalf of, as <see cref="BitwardenHeaders.User"/> names it.
    /// </summary>
    public const string UserClaim = "context_user";

    /// <summary>
    /// The provider on whose authority the call is made, as <see cref="BitwardenHeaders.Provider"/> names it.
    /// </summary>
    public const string ProviderClaim = "context_provider";

    /// <summary>
    /// The <see cref="BitwardenHeaders.Context"/> object, as the string the header carries.
    /// </summary>
    public const string ContextClaim = "context";

    /// <summary>
    /// Whether the context was <see cref="Delegated"/> or <see cref="Asserted"/>.
    /// </summary>
    public const string ModeClaim = "ctx_mode";

    /// <summary>
    /// The calling service, which the token is bound to.
    /// </summary>
    public const string ClientClaim = "client_id";

    /// <summary>
    /// RFC 8693's actor claim: the services the call passed through, newest outermost.
    /// </summary>
    public const string ActorClaim = "act";

    /// <summary>
    /// Proven by a person's, organization's or machine account's own token, agreeing with what was
    /// asserted at every hop.
    /// </summary>
    public const string Delegated = "DELEGATED";

    /// <summary>
    /// Stated by the calling service on its own authority.
    /// </summary>
    public const string Asserted = "ASSERTED";

    /// <summary>
    /// The token request's parameter for each owner and for the context object.
    /// </summary>
    public static class Parameters
    {
        /// <summary>
        /// The organization: <see cref="BitwardenHeaders.Organization"/>'s value.
        /// </summary>
        public const string Organization = "context_organization";

        /// <summary>
        /// The user: <see cref="BitwardenHeaders.User"/>'s value.
        /// </summary>
        public const string User = "context_user";

        /// <summary>
        /// The provider: <see cref="BitwardenHeaders.Provider"/>'s value.
        /// </summary>
        public const string Provider = "context_provider";

        /// <summary>
        /// The context object: <see cref="BitwardenHeaders.Context"/>'s value.
        /// </summary>
        public const string Context = "context";
    }
}
