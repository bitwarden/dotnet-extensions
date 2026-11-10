namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// The organization or provider an operation names in its path or body when the person's session
/// names none, for the operation's outgoing calls to carry.
/// </summary>
/// <remarks>
/// <para>
/// A few operations concern an organization the person can't hold a session for yet, such as
/// accepting an invitation. Those name it in the request, and the service they call decides with
/// its own authority whether to act on it.
/// </para>
/// <para>
/// The value never enters the receiving service's own <see cref="IContext"/>. Implemented by the
/// hosting package; with no implementation registered, nothing is named.
/// </para>
/// </remarks>
public interface IRequestNamedOwner
{
    /// <summary>
    /// The organization the operation names, when it is declared to name one and the session names
    /// none.
    /// </summary>
    Guid? OrganizationId { get; }

    /// <summary>
    /// The provider the operation names, when it is declared to name one and the session names none.
    /// </summary>
    Guid? ProviderId { get; }
}
