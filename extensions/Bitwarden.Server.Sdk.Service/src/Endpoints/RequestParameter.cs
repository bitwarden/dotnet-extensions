using System.Reflection;

namespace Bitwarden.Server.Sdk.Service.Endpoints;

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
    /// The parameter's own name, which an MVC controller's messages call it by.
    /// </summary>
    public string DisplayName => Info.Name ?? ModelName;
}
