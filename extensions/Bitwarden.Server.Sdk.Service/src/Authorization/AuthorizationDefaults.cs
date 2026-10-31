namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// What a service's operations demand unless they say otherwise.
/// </summary>
/// <remarks>
/// <para>
/// Declared once on the service rather than on every operation. Most of what an
/// <see cref="AuthorizationAttribute"/> carries is uniform across an endpoints class and often across
/// a whole service: a vault is mostly personal, so nearly every operation in it accepts a
/// request naming no organization, and saying so a hundred times is a hundred chances to leave
/// one out.
/// </para>
/// <para>
/// Three levels, most specific winning: this, then the <see cref="AuthorizationAttribute"/> on
/// the class declaring the handler, then the operation's. An operation that matches its
/// service in every respect needs no attribute at all.
/// </para>
/// <para>
/// Only the parts that are genuinely uniform. A role or a permission is about what one
/// operation demands of the caller, and a service-wide <c>ManageUsers</c> applied to a read
/// would be wrong in a way nobody would see.
/// </para>
/// </remarks>
public sealed record AuthorizationDefaults
{
    /// <summary>
    /// Whether a request must name an organization.
    /// </summary>
    public ContextRequirement Organization { get; init; } = ContextRequirement.Required;

    /// <summary>
    /// Whether a request must name the user it is made for.
    /// </summary>
    public ContextRequirement User { get; init; } = ContextRequirement.Optional;

    /// <summary>
    /// Whether a request must name a provider.
    /// </summary>
    /// <remarks>
    /// Forbidden unless declared: <c>Bitwarden-Provider</c> travels only when the
    /// context has a provider, and an operation that answers for one says so, <c>Optional</c> or
    /// <c>Required</c>, so a provider never reaches an operation that never meant to answer for one.
    /// </remarks>
    public ContextRequirement Provider { get; init; } = ContextRequirement.Forbidden;

    /// <summary>
    /// The scope an operation that only reads demands.
    /// </summary>
    /// <remarks>
    /// Applied to every operation the platform can see is a read. Left unset, an operation that
    /// declares no scope of its own demands none, so a service should set it.
    /// </remarks>
    public string? ReadScope { get; init; }

    /// <summary>
    /// The scope an operation that changes something demands.
    /// </summary>
    /// <remarks>
    /// Everything that is not a read: creates, updates, replaces, deletes, actions and every
    /// bulk form of them. The platform knows which an operation is from the attribute it
    /// carries, so a service declaring these two has scope enforced everywhere without an
    /// operation repeating it.
    /// </remarks>
    public string? WriteScope { get; init; }
}
