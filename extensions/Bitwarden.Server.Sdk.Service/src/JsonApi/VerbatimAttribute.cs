namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// Marks a string member the binding passes on as the caller sent it: not trimmed, and a blank
/// value kept rather than read as absent.
/// </summary>
/// <remarks>
/// For a member whose checks must see exactly what was sent, such as one in a contract whose rules
/// a service must reproduce: a blank value there can fail a length rule as well as a required one,
/// and surrounding spaces count towards its length. Absent and <c>null</c> are still absent. The
/// operation checks such a member itself; the binding does not clean it.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class VerbatimAttribute : Attribute;
