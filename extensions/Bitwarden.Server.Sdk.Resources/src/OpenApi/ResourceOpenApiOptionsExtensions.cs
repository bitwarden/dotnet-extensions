using Microsoft.AspNetCore.OpenApi;

namespace Bitwarden.Server.Sdk.Resources.OpenApi;

/// <summary>
/// Describes resources and their standard operations in a service's OpenAPI document.
/// </summary>
public static class ResourceOpenApiOptionsExtensions
{
    /// <summary>
    /// Describes the wire shape rather than the flat records: schemas named after the domain, resources in their
    /// envelope, the standard responses and errors, paging, sparse fields, filtering and sorting, and sections named
    /// for a reader.
    /// </summary>
    /// <remarks>
    /// Add it after transformers that work on the flat schema, such as those deciding from nullability what is
    /// required, and before anything that orders the document.
    /// </remarks>
    /// <param name="options">The document's options.</param>
    /// <returns>The options.</returns>
    public static OpenApiOptions DescribeResources(this OpenApiOptions options)
    {
        // Names the document schemas after the domain rather than after the platform's generics.
        options.CreateSchemaReferenceId = SchemaNames.For;

        options.AddSchemaTransformer<ReadOnlySchemaTransformer>();
        options.AddSchemaTransformer<DefaultStringLengthSchemaTransformer>();
        options.AddSchemaTransformer<ResourceSchemaTransformer>();
        options.AddSchemaTransformer<ResourceDocumentSchemaTransformer>();
        options.AddDocumentTransformer<ResourceEnvelopeDocumentTransformer>();
        options.AddOperationTransformer<StandardResponsesOperationTransformer>();
        options.AddOperationTransformer<BulkAtomicityOperationTransformer>();
        options.AddDocumentTransformer<StandardErrorResponseDocumentTransformer>();
        options.AddDocumentTransformer<PagingDocumentTransformer>();
        options.AddDocumentTransformer<FieldsDocumentTransformer>();
        options.AddDocumentTransformer<FilteringDocumentTransformer>();
        options.AddDocumentTransformer<SortingDocumentTransformer>();
        options.AddDocumentTransformer<PageMetaDocumentTransformer>();
        options.AddDocumentTransformer<RequestBodyDescriptionDocumentTransformer>();

        // Before the ordering, which sorts by the published name rather than the type name.
        options.AddDocumentTransformer<TagDocumentTransformer>();

        return options;
    }
}
