using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Describes an enumeration as the string it actually is.
/// </summary>
/// <remarks>
/// The generator publishes the permitted values but not the type they are, which leaves a field
/// whose schema lists <c>USER</c> and <c>ADMIN</c> without ever saying it is a string. Some
/// generators infer it; others emit an untyped field or refuse the document. Saying so costs one
/// keyword.
/// </remarks>
internal sealed class EnumSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;

        if (!type.IsEnum)
        {
            return Task.CompletedTask;
        }

        // A nullable enum is a string or null. Stated as the union, which the 3.0 writer turns
        // into `nullable: true`; the generator otherwise signals it by putting a null among the
        // permitted values, which is then lost when the values below are restated.
        schema.Type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) is null
            ? JsonSchemaType.String
            : JsonSchemaType.String | JsonSchemaType.Null;

        // Restated from the naming policy rather than left as the generator found them: the
        // generator reads the CLR member names, which are not what goes on the wire.
        schema.Enum = [.. EnumValues.Of(type).Select(value => (JsonNode)JsonValue.Create(value))];

        return Task.CompletedTask;
    }
}
