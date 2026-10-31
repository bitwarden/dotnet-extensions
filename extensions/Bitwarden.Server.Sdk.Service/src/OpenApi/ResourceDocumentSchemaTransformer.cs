using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Rebuilds the schema of a resource document, which its converter would otherwise leave opaque.
/// </summary>
/// <remarks>
/// A type carrying a <see cref="System.Text.Json.Serialization.JsonConverter"/> reports no
/// properties through System.Text.Json's metadata, so the generator emits an empty schema for it.
/// A document has only two members, so describing them by hand costs little, and it is the reason
/// the envelope is applied to the document rather than to the resource, whose schema could not be
/// rebuilt so cheaply.
/// </remarks>
internal sealed class ResourceDocumentSchemaTransformer : IOpenApiSchemaTransformer
{
    public async Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var documentType = context.JsonTypeInfo.Type;

        if (!documentType.IsGenericType)
        {
            return;
        }

        var definition = documentType.GetGenericTypeDefinition();

        // A bulk create or replace carries its resources as a collection does, and nothing else:
        // what the caller asks about atomicity travels in the query, not the body.
        if (definition == typeof(BulkCreate<>) || definition == typeof(BulkReplace<>))
        {
            var items = await context.GetOrCreateSchemaAsync(documentType.GetGenericArguments()[0], null, cancellationToken);

            schema.Type = JsonSchemaType.Object;
            schema.Properties = new Dictionary<string, IOpenApiSchema>
            {
                [ResourceEnvelope.Data] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Items = items,
                    Description = definition == typeof(BulkCreate<>)
                        ? "The resources to create, each shaped as the single create expects it. Each "
                          + "may also carry \"lid\", beside \"type\": a non-empty string of the caller's "
                          + "choosing, unique within the request, echoed on the resource created from it."
                        : null,
                },
            };
            schema.Required = new HashSet<string> { ResourceEnvelope.Data };

            return;
        }

        // What a bulk create made: the resources in request order, how many, and each item's lid
        // echoed. The lid is described rather than added to the resource's schema, which every
        // other operation shares and where it would promise a field that never arrives.
        if (definition == typeof(BulkCreatedDocument<>))
        {
            var created = await context.GetOrCreateSchemaAsync(documentType.GetGenericArguments()[0], null, cancellationToken);

            schema.Type = JsonSchemaType.Object;
            schema.Properties = new Dictionary<string, IOpenApiSchema>
            {
                [ResourceEnvelope.Data] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Items = created,
                    Description = "The created resources, in the order the request named them. Each "
                        + "carries the \"lid\" its item was sent with, beside \"id\" and \"type\", "
                        + "when it was sent with one.",
                },
                ["meta"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Description = "Information about the response rather than the resources in it. "
                        + "May carry fields beyond those named here.",
                    ReadOnly = true,
                    AdditionalProperties = new OpenApiSchema(),
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["affectedCount"] = Count("How many resources were created."),
                    },
                    Required = new HashSet<string> { "affectedCount" },
                },
            };
            schema.Required = new HashSet<string> { ResourceEnvelope.Data, "meta" };

            return;
        }

        var single = definition == typeof(ResourceDocument<>);
        var paged = definition == typeof(PagedResourceListDocument<>);
        var cursor = definition == typeof(CursorPagedResourceListDocument<>);

        if (!single && !paged && !cursor && definition != typeof(ResourceListDocument<>))
        {
            return;
        }

        var resource = documentType.GetGenericArguments()[0];
        var resourceSchema = await context.GetOrCreateSchemaAsync(resource, null, cancellationToken);

        schema.Type = JsonSchemaType.Object;
        schema.Properties = new Dictionary<string, IOpenApiSchema>
        {
            [ResourceEnvelope.Data] = single
                ? resourceSchema
                : new OpenApiSchema { Type = JsonSchemaType.Array, Items = resourceSchema },
            ["meta"] = Meta(
                paged ? Paging.Offset : cursor ? Paging.Cursor : Paging.None,
                IsReadOnly(documentType, nameof(ResourceDocument<Resource>.Meta))),
        };

        // Only a paged collection says where the rest of it is. An unpaged one has already
        // returned all of it, and a single resource has no next page.
        if (paged)
        {
            schema.Properties["links"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Description = "Where the rest of the collection is. A link that would not go "
                    + "anywhere is absent.",
                ReadOnly = true,
                Properties = new Dictionary<string, IOpenApiSchema>
                {
                    ["first"] = Link("The first page."),
                    ["prev"] = Link("The page before this one."),
                    ["next"] = Link("The page after this one."),
                    ["last"] = Link("The last page."),
                },
            };
        }

        schema.Required = new HashSet<string> { ResourceEnvelope.Data };
    }

    /// <summary>
    /// Whether a document member is declared <see cref="ReadOnlyAttribute"/>.
    /// </summary>
    /// <summary>
    /// The response's own information: what the platform always puts there, and room for more.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Open-ended by design (a service may add whatever describes its response), but an
    /// open-ended object documented as nothing at all tells a caller less than it could. The
    /// fields the platform populates are named, and <c>additionalProperties</c> keeps the rest
    /// open, so a caller knows what it may rely on without being told that is all there is.
    /// </para>
    /// <para>
    /// Named on a paged read only. An unpaged collection has nothing to count, and a single
    /// resource has nothing to say; describing either as carrying a total would promise a
    /// field that never arrives.
    /// </para>
    /// </remarks>
    private static OpenApiSchema Meta(Paging paging, bool readOnly)
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object | JsonSchemaType.Null,
            Description = "Information about the response rather than the resources in it. "
                + "May carry fields beyond those named here.",

            // Rebuilding the schema discards whatever ReadOnlySchemaTransformer set, so the
            // member's own declaration is consulted again here rather than assumed.
            ReadOnly = readOnly,
            // Stated rather than left to the default. Omitting it means the same thing, but
            // a reader of the document should not have to know that to be sure.
            AdditionalProperties = new OpenApiSchema(),
        };

        if (paging == Paging.Offset)
        {
            schema.Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["pageCount"] = Count("How many pages there are. At least one: an empty collection is one empty page."),
                ["pageNumber"] = Count("Which page this is, from 1."),
                ["pageSize"] = Count("How many resources a page holds."),
                ["totalCount"] = Count("How many resources match, across every page."),
            };
            schema.Required = new HashSet<string> { "pageCount", "pageNumber", "pageSize", "totalCount" };
        }

        if (paging == Paging.Cursor)
        {
            schema.Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["pageSize"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer,
                    Format = "int32",
                    Description = "How many resources the page could hold.",
                },
                ["hasMore"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Boolean,
                    Description = "Whether more resources follow this page.",
                },
                ["cursor"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Description = "What to send as `page[after]` for the next page. Opaque: pass it "
                        + "back unchanged. Absent on the last page.",
                },
            };
            schema.Required = new HashSet<string> { "pageSize", "hasMore" };
        }

        return schema;
    }

    /// <summary>
    /// How a collection is paged, which decides what its meta names.
    /// </summary>
    private enum Paging
    {
        None,
        Offset,
        Cursor,
    }

    private static OpenApiSchema Count(string description) =>
        new() { Type = JsonSchemaType.Integer, Format = "int32", Description = description };

    private static OpenApiSchema Link(string description) =>
        new() { Type = JsonSchemaType.String, Description = description };

    private static bool IsReadOnly(Type documentType, string member) =>
        documentType.GetProperty(member)?.IsDefined(typeof(ReadOnlyAttribute), inherit: true) == true;
}
