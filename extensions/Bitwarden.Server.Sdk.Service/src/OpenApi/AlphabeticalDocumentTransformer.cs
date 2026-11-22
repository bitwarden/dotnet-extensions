using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Puts the document's tags and paths in alphabetical order.
/// </summary>
/// <remarks>
/// <para>
/// Left alone, both come out in discovery order, which is the order the service happened to map
/// its endpoints in. That is arbitrary to a reader, so the documentation UI presents its sections
/// in no order anyone can predict, and it is unstable to a reviewer: adding one endpoints class
/// can move every section and turn a one-line change into a whole-file diff.
/// </para>
/// <para>
/// The document is committed to source control, so its stability is part of its usefulness.
/// </para>
/// </remarks>
internal sealed class AlphabeticalDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (document.Tags is { Count: > 1 })
        {
            var ordered = document.Tags.OrderBy(tag => tag.Name, StringComparer.Ordinal).ToArray();

            document.Tags.Clear();

            foreach (var tag in ordered)
            {
                document.Tags.Add(tag);
            }
        }

        if (document.Paths is { Count: > 1 })
        {
            var ordered = document.Paths.OrderBy(path => path.Key, StringComparer.Ordinal).ToArray();

            document.Paths.Clear();

            foreach (var (path, item) in ordered)
            {
                document.Paths.Add(path, item);
            }
        }

        return Task.CompletedTask;
    }
}
