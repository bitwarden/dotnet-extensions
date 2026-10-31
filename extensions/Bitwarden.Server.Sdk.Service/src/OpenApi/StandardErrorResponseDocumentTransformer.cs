using Bitwarden.Server.Sdk.Common.Errors;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Gives every operation one <c>default</c> response describing the shared failure shape.
/// </summary>
/// <remarks>
/// <para>
/// Runs at the document stage because that is the only stage that can both generate
/// <see cref="ErrorResponse"/>'s schema and register it as a component. Asking for it while
/// transforming an operation returns an empty placeholder: by then the generator's schema store is
/// settled, and a type no endpoint signature mentions was never put in it.
/// </para>
/// <para>
/// The effect on a generated client is the point: two branches per operation, the declared success
/// type and <see cref="ErrorResponse"/> for everything else, rather than a per-status list that is
/// copy-pasted, drifts, and can never be complete.
/// </para>
/// </remarks>
internal sealed class StandardErrorResponseDocumentTransformer : IOpenApiDocumentTransformer
{
    private const string SchemaName = nameof(ErrorResponse);
    private const string ErrorSchemaName = nameof(Error);
    private const string SourceSchemaName = nameof(ErrorSource);

    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (document.Paths is null)
        {
            return;
        }

        var schema = await context.GetOrCreateSchemaAsync(typeof(ErrorResponse), null, cancellationToken);

        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        document.Components.Schemas[SchemaName] = schema;

        Name(document, schema);

        var reference = new OpenApiSchemaReference(SchemaName, document);

        foreach (var operation in document.Paths.Values
                     .Where(path => path.Operations is not null)
                     .SelectMany(path => path.Operations!.Values))
        {
            (operation.Responses ??= new OpenApiResponses())["default"] = new OpenApiResponse
            {
                Description = "The request did not succeed. The body describes why.",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new() { Schema = reference },
                },
            };
        }
    }

    /// <summary>
    /// Lifts the objects nested inside the error document into schemas of their own.
    /// </summary>
    /// <remarks>
    /// Building this schema at the document stage produces it fully inlined: nothing here went
    /// through the generator's reference machinery, so <see cref="Error"/> and
    /// <see cref="ErrorSource"/> arrive as anonymous objects even though both are declared types.
    /// A client generator then invents names for them from the members that hold them, and every
    /// Bitwarden client ends up publishing types called <c>Errors</c> and <c>Source</c>.
    /// </remarks>
    private static void Name(OpenApiDocument document, IOpenApiSchema response)
    {
        if (response is not OpenApiSchema { Properties: not null } document_
            || !document_.Properties.TryGetValue("errors", out var errors)
            || errors is not OpenApiSchema { Items: OpenApiSchema error } list)
        {
            return;
        }

        if (error.Properties?.TryGetValue("source", out var source) == true && source is OpenApiSchema)
        {
            document.Components!.Schemas![SourceSchemaName] = source;
            error.Properties["source"] = new OpenApiSchemaReference(SourceSchemaName, document);
        }

        document.Components!.Schemas![ErrorSchemaName] = error;
        list.Items = new OpenApiSchemaReference(ErrorSchemaName, document);
    }
}
