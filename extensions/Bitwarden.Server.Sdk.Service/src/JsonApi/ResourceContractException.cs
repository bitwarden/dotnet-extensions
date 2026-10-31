using System.Text.Json;

namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// A request document broke the resource contract in a way that names a specific field.
/// </summary>
/// <remarks>
/// A <see cref="JsonException"/> thrown from a converter arrives in model state keyed <c>$</c>
/// (the document root) because the converter parses the document itself and is outside the
/// serializer's path tracking. Carrying the pointer on the exception lets the error report the
/// field that actually offended, which the standards ask for.
/// </remarks>
/// <param name="message">What was wrong, in prose.</param>
/// <param name="pointer">A JSON Pointer to the offending field.</param>
internal sealed class ResourceContractException(string message, string pointer)
    : JsonException(message, PathOf(pointer), null, null)
{
    /// <summary>
    /// The pointer as a JSON path: <c>/data/1/lid</c> as <c>$.data[1].lid</c>.
    /// </summary>
    /// <remarks>
    /// Set on the exception because a body the default input formatter reads (a bulk create,
    /// which no resource binder handles) reaches model state as its message keyed by the
    /// exception's path, and the exception itself is not kept. The serializer adds a path only
    /// where none is set, so this one survives, and the error factory turns it back into the
    /// pointer.
    /// </remarks>
    private static string PathOf(string pointer) =>
        "$" + string.Concat(pointer
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => int.TryParse(segment, out _) ? $"[{segment}]" : $".{segment}"));

    /// <summary>
    /// A JSON Pointer to the offending field.
    /// </summary>
    public string Pointer { get; } = pointer;
}
