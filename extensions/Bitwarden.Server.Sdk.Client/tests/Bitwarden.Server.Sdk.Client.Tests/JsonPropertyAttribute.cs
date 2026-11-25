namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// Stands in for Newtonsoft's attribute, which the helper recognizes by name.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class JsonPropertyAttribute(string propertyName) : Attribute
{
    public string PropertyName { get; } = propertyName;

    /// <summary>
    /// Newtonsoft's <c>Required</c>, as its number: 2 is <c>Always</c>.
    /// </summary>
    public int Required { get; set; }
}
