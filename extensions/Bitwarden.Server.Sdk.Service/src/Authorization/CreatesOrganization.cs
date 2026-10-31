namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Declares that a product service's operation creates an organization, and so may name the
/// organization it created on its own downstream calls
/// (the Client package's <c>OutgoingCall.AboutCreatedOrganization</c>).
/// </summary>
/// <remarks>
/// <para>
/// A product service's downstream calls carry the person and organization the session names, and
/// it may not name others. A request creating an organization
/// names none, yet its orchestration must speak about the new one: what its plan entitles it to,
/// its first collection. The orchestration belongs in the product layer, so this is the narrow way
/// for it to say which organization those calls are about.
/// </para>
/// <para>
/// On such an operation, after the domain service that owns organizations has created one, the
/// operation may state it on its calls. The platform allows that only here, only while the request
/// itself names no organization, and only for one organization per request: the first one stated
/// is the only one any later call may name. The operation must forbid an organization and require a
/// person; the service refuses to start otherwise.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CreatesOrganizationAttribute : Attribute;
