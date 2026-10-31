using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Gives the paging metadata a name, once.
/// </summary>
/// <remarks>
/// <para>
/// Left inline, a generator invents a name for it, and every Bitwarden client would publish a
/// type called <c>Meta</c> in its consumer's namespace, which is the same problem the attributes
/// object had.
/// </para>
/// <para>
/// One shared schema rather than one per resource, because it is the same shape everywhere: the
/// total, and room for whatever else a service puts there. A client then has a single
/// <c>PageMeta</c> instead of one per collection.
/// </para>
/// <para>
/// A document transformer, because a reference created while schemas are still being built does
/// not survive into the finished document.
/// </para>
/// </remarks>
internal sealed class PageMetaDocumentTransformer : IOpenApiDocumentTransformer
{
    private const string SchemaName = "PageMeta";
    private const string Suffix = "PagedListDocument";
    private const string CursorSchemaName = "CursorPageMeta";
    private const string CursorSuffix = "CursorListDocument";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (document.Components?.Schemas is null)
        {
            return Task.CompletedTask;
        }

        Share(document, Suffix, SchemaName);
        Share(document, CursorSuffix, CursorSchemaName);

        return Task.CompletedTask;
    }

    /// <remarks>
    /// Taken from the first document of each kind, because the platform builds them all the same
    /// way, and every one of that kind then refers to it.
    /// </remarks>
    private static void Share(OpenApiDocument document, string suffix, string name)
    {
        var schemas = document.Components!.Schemas!;

        var documents = schemas
            .Where(entry => entry.Key.EndsWith(suffix, StringComparison.Ordinal))
            .Select(entry => entry.Value)
            .OfType<OpenApiSchema>()
            .Where(schema => schema.Properties?.ContainsKey("meta") == true)
            .ToArray();

        if (documents.Length == 0)
        {
            return;
        }

        schemas[name] = documents[0].Properties!["meta"];

        foreach (var schema in documents)
        {
            schema.Properties!["meta"] = new OpenApiSchemaReference(name, document);
        }
    }
}
