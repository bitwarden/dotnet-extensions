namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// The request headers that carry Bitwarden's caller context from one service to another.
/// </summary>
/// <remarks>
/// <para>
/// Between services only. A browser sends its token and the standard Bitwarden client headers,
/// nothing else: a product service takes the person, the organization and the provider from the
/// session it validated, and refuses a request carrying any of these four before authentication.
/// An edge proxy in front of the services should strip them from every request it forwards. A
/// domain service believes them only from another service, a caller presenting an internal service
/// token (<c>client_id</c> <c>internal.*</c>); for anybody else, a person's token, a Send's token or
/// no token at all, its context is empty.
/// </para>
/// <para>
/// These are context, not authentication. Who the caller <em>is</em> comes from its token; these say
/// which organization a request concerns and which user it is made on behalf of. A service must
/// never treat their presence as authorization: the calling service asserts them and they are not
/// signed, so the owning service still enforces its own access rules.
/// </para>
/// <para>
/// No <c>X-</c> prefix (RFC 6648), as Bitwarden's existing <c>Bitwarden-Client-Name</c> header has none.
/// </para>
/// </remarks>
public static class BitwardenHeaders
{
    /// <summary>The organization a request concerns. Absent for data belonging to no organization.</summary>
    public const string Organization = "Bitwarden-Organization";

    /// <summary>The user a request is made on behalf of. Absent for background work with no user.</summary>
    public const string User = "Bitwarden-User";

    /// <summary>The provider on whose authority the operation is made, if any.</summary>
    /// <remarks>
    /// <para>
    /// A third axis, not an alternative to the other two. Sent only when the context has a provider:
    /// a provider's session (the Provider Portal's), or a message published from one. An operation
    /// that takes it declares so (<c>Provider</c> on its authorization, forbidden unless declared),
    /// so a provider can't reach an operation that never meant to answer for one.
    /// </para>
    /// </remarks>
    public const string Provider = "Bitwarden-Provider";

    /// <summary>
    /// Everything else a callee may need about where a request came from and who acted: one closed,
    /// versioned JSON object (<see cref="CallerContextHeader"/>).
    /// </summary>
    /// <remarks>
    /// Written only by the platform's outgoing handler, from the context the calling service holds,
    /// and parsed strictly: an unknown member, a member twice, a value of the wrong kind, or more
    /// than <see cref="CallerContextHeader.MaximumLength"/> bytes, and the request is refused
    /// <c>400</c>.
    /// </remarks>
    public const string Context = "Bitwarden-Context";

    /// <summary>
    /// The four headers above, which a service holding people's sessions refuses from outside and
    /// an edge proxy strips from every request it forwards.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = [Organization, User, Provider, Context];
}
