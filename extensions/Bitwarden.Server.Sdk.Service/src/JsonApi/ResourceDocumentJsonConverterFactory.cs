using System.Text.Json;
using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// Applies the JSON:API <c>attributes</c> envelope to the resources inside a document.
/// </summary>
/// <remarks>
/// <para>
/// The envelope is applied at the <em>document</em> level rather than on <see cref="Resource"/>
/// itself, and that placement is deliberate. A type carrying a <see cref="JsonConverter"/> reports
/// no properties through System.Text.Json's metadata, so the OpenAPI generator cannot see its shape
/// and emits an opaque schema. Converting the document instead leaves every resource introspectable
/// (its schema keeps its properties, data annotations and XML documentation) and costs only the
/// documents' own schemas, which are two members each and are rebuilt by
/// <see cref="OpenApi.ResourceDocumentSchemaTransformer"/>.
/// </para>
/// <para>
/// So a resource is declared flat in C#, serialised enveloped on the wire, and described enveloped
/// in the OpenAPI document, with no hand-written attributes class to drift.
/// </para>
/// </remarks>
public sealed class ResourceDocumentJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsGenericType
           && (typeToConvert.GetGenericTypeDefinition() == typeof(ResourceDocument<>)
               || typeToConvert.GetGenericTypeDefinition() == typeof(ResourceListDocument<>)
               || typeToConvert.GetGenericTypeDefinition() == typeof(PagedResourceListDocument<>)
               || typeToConvert.GetGenericTypeDefinition() == typeof(CursorPagedResourceListDocument<>)
               || typeToConvert.GetGenericTypeDefinition() == typeof(BulkCreate<>)
               || typeToConvert.GetGenericTypeDefinition() == typeof(BulkCreatedDocument<>)
               || typeToConvert.GetGenericTypeDefinition() == typeof(BulkReplace<>));

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var resource = typeToConvert.GetGenericArguments()[0];

        var definition = typeToConvert.GetGenericTypeDefinition();

        var converter = definition == typeof(ResourceDocument<>) ? typeof(ResourceDocumentJsonConverter<>)
            : definition == typeof(ResourceListDocument<>) ? typeof(ResourceListJsonConverter<>)
            : definition == typeof(CursorPagedResourceListDocument<>) ? typeof(CursorPagedResourceListJsonConverter<>)
            : definition == typeof(BulkCreate<>) ? typeof(BulkCreateJsonConverter<>)
            : definition == typeof(BulkCreatedDocument<>) ? typeof(BulkCreatedJsonConverter<>)
            : definition == typeof(BulkReplace<>) ? typeof(BulkReplaceJsonConverter<>)
            : typeof(PagedResourceListJsonConverter<>);

        return (JsonConverter)Activator.CreateInstance(converter.MakeGenericType(resource))!;
    }
}
