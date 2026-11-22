using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Bitwarden.Server.Sdk.Resources.Endpoints;

/// <summary>
/// One handler parameter, classified once when its endpoint is built.
/// </summary>
/// <param name="Index">Its position among the handler's arguments.</param>
/// <param name="Info">The parameter.</param>
/// <param name="Kind">Where it comes from.</param>
/// <param name="ModelName">The name the caller sends it under, and errors are keyed by.</param>
/// <param name="Place">For a simple value, where it is read from.</param>
/// <param name="Required">Whether a request without it is refused.</param>
internal sealed record RequestParameter(
    int Index,
    ParameterInfo Info,
    ParameterKind Kind,
    string ModelName,
    ValuePlace Place,
    bool Required)
{
    /// <summary>
    /// The type, with any <see cref="Nullable{T}"/> unwrapped.
    /// </summary>
    public Type Underlying => Nullable.GetUnderlyingType(Info.ParameterType) ?? Info.ParameterType;

    /// <summary>
    /// What an MVC controller's messages call the parameter: the name its <see cref="DisplayAttribute"/> gives, or its
    /// own.
    /// </summary>
    public string DisplayName { get; } = Info.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? Info.Name ?? ModelName;

    /// <summary>
    /// The validation attributes the parameter itself carries, which a request's value for it must satisfy.
    /// </summary>
    public IReadOnlyList<ValidationAttribute> Validators { get; } = [.. Info.GetCustomAttributes(inherit: true).OfType<ValidationAttribute>()];
}
