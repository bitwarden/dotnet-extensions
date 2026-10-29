namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Declares that a resource's attribute is personal data: about an identifiable person, such as
/// an address or a name, though not Protected Data (that is <see cref="EncryptedAttribute"/>).
/// </summary>
/// <remarks>
/// <para>
/// A marker, read where a copy of the attribute could reach more readers than the resource does.
/// A service's cache is one: a resource its service reads across owners
/// (<c>AcrossOwners</c>) is answered from the cache to any holder of the read's scope, so such a
/// resource may not cache an attribute marked here, and the service refuses to start if it is
/// declared to (the server repository's <c>src/Core/Utilities/CACHING.md</c>, section 7).
/// </para>
/// <para>
/// Marking changes nothing a caller sees and nothing the service validates.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// /// &lt;summary&gt;The address the invitation was sent to.&lt;/summary&gt;
/// [PersonalData]
/// public required string Email { get; init; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class PersonalDataAttribute : Attribute;
