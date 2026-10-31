using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// Caches, per caller-supplied options instance, a copy with the document converter removed.
/// </summary>
/// <remarks>
/// A document converter serialises its own document type through the ordinary machinery, which
/// would re-enter the converter and recurse forever. Removing the factory from a copy breaks that
/// cycle. The copy is cached because System.Text.Json builds and caches type metadata per options
/// instance, so creating one per call would defeat that cache.
/// </remarks>
internal static class FlattenedOptions
{
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> Cache = new();

    public static JsonSerializerOptions For(JsonSerializerOptions options) =>
        Cache.GetValue(options, static source =>
        {
            var flattened = new JsonSerializerOptions(source);

            for (var i = flattened.Converters.Count - 1; i >= 0; i--)
            {
                if (flattened.Converters[i] is ResourceDocumentJsonConverterFactory)
                {
                    flattened.Converters.RemoveAt(i);
                }
            }

            return flattened;
        });
}
