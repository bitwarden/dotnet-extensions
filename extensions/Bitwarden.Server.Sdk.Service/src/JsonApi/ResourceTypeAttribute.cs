namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// Overrides the resource type a <see cref="Resource"/> would otherwise derive from its own name.
/// </summary>
/// <remarks>
/// Needed only where the record's name is not the wire name. Regular resources should carry no
/// attribute at all.
/// </remarks>
/// <param name="type">
/// The resource type: the singular resource name, in lowercase kebab-case.
/// </param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ResourceTypeAttribute(string type) : Attribute
{
    /// <summary>
    /// The resource type to use.
    /// </summary>
    public string Type { get; } = type;
}
