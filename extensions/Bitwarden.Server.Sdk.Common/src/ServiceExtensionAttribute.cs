using System.Diagnostics.CodeAnalysis;

namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Declares that an assembly carries an <see cref="IServiceExtension"/> every service referencing
/// it should apply.
/// </summary>
/// <remarks>
/// Declared on the assembly rather than discovered by scanning, so an extension is something a
/// package author states on purpose, where a reviewer will see it.
/// </remarks>
/// <param name="extension">The extension's type. Must have a public parameterless constructor.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ServiceExtensionAttribute(
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type extension)
    : Attribute
{
    /// <summary>
    /// The extension's type.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type Extension { get; } = extension;
}
