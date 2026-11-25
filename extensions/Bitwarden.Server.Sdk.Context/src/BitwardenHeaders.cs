namespace Bitwarden.Server.Sdk.Context;

/// <summary>
/// The request headers that carry context from one service to another.
/// </summary>
/// <remarks>
/// <para>
/// Between services only. A service that holds people's sessions takes the organization, user and
/// provider from the session it validated, and refuses a request from outside that carries any of
/// these headers. An edge proxy should strip them from every request it forwards.
/// </para>
/// <para>
/// These are context, not authentication: who the caller is comes from its token. A host believes
/// them only as <see cref="Delegation"/> signs them, and refuses a plain header that disagrees. A
/// context a service asserted on its own authority is still only that service's word, so the
/// receiving service enforces its own access rules.
/// </para>
/// </remarks>
public static class BitwardenHeaders
{
    /// <summary>
    /// The organization a request concerns. Absent for data belonging to no organization.
    /// </summary>
    public const string Organization = "Bitwarden-Organization";

    /// <summary>
    /// The user a request is made on behalf of. Absent for background work with no user.
    /// </summary>
    public const string User = "Bitwarden-User";

    /// <summary>
    /// The provider on whose authority the operation is made, if any.
    /// </summary>
    public const string Provider = "Bitwarden-Provider";

    /// <summary>
    /// Everything else a callee may need about where a request came from and who acted, as one
    /// closed, versioned JSON object written and read by <see cref="ContextHeader"/>.
    /// </summary>
    public const string Context = "Bitwarden-Context";

    /// <summary>
    /// The signed form of the four headers above: a <see cref="ContextToken"/> issued for this
    /// call's caller and callee.
    /// </summary>
    public const string Delegation = "Bitwarden-Delegation";

    /// <summary>
    /// The five headers above.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = [Organization, User, Provider, Context, Delegation];

    /// <summary>
    /// The services the request passed through before this call, oldest first, comma-separated (<see cref="CallChain"/>).
    /// </summary>
    /// <remarks>
    /// Not context and not in <see cref="All"/>: it says nothing of whom a request concerns, is not signed, and
    /// grants nothing. A service reads it only from another service's authenticated call, ignores it from anybody
    /// else, and uses it only to notice being called back while its own request is open.
    /// </remarks>
    public const string CallChain = "Bitwarden-Call-Chain";
}
