namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// The organization or provider a product service's operation names in its path or body, where
/// the person's session names none: what the operation's outgoing calls carry in its place.
/// </summary>
/// <remarks>
/// <para>
/// A person's session names the organization or provider it was issued for, and a product service
/// takes its context from there and nowhere else. A few operations are about an
/// organization or provider the person can't hold a session for yet: accepting an invitation,
/// reading the policies of an organization they have accepted but are not confirmed in, reading an
/// organization's public key before joining. Those name it in the request itself, the path when it
/// names the resource and the body when it is an action's input, and declare that they do; the
/// domain service they call decides, with its own authority (the invitation's token, the membership).
/// </para>
/// <para>
/// What this answers never enters the product service's own <see cref="IContext"/>: the caller holds
/// no role or permission there, and the operation's authorization never sees it. It is carried only
/// on the outgoing calls, by the Client package's handler, when the request's context names none.
/// Implemented by the hosting package, which knows the routed operation; with no implementation
/// registered, nothing is named.
/// </para>
/// </remarks>
public interface IRequestNamedOwner
{
    /// <summary>The organization the operation names, when it is declared to name one and the request's session names none.</summary>
    Guid? OrganizationId { get; }

    /// <summary>The provider the operation names, when it is declared to name one and the request's session names none.</summary>
    Guid? ProviderId { get; }
}
