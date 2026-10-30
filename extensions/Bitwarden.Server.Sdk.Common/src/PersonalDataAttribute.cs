namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Declares that a resource's attribute is personal data about an identifiable person, such as a
/// name or an address, but not Protected Data (see <see cref="EncryptedAttribute"/>).
/// </summary>
/// <remarks>
/// A marker, read wherever a copy of the attribute could reach more readers than the resource
/// does, such as a cache shared across owners. Marking changes nothing a caller sees and nothing
/// the service validates.
/// </remarks>
/// <example>
/// <code>
/// [PersonalData]
/// public required string Email { get; init; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class PersonalDataAttribute : Attribute;
