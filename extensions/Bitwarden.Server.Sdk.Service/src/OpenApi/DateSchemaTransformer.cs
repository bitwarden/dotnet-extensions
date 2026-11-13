using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Describes a date as a string in <c>date-time</c> format, nullable where the property is.
/// </summary>
/// <remarks>
/// The service writes dates through its own converters (<see cref="UtcDates"/>: UTC
/// with a literal <c>Z</c>), and the schema exporter can't see inside a custom converter, so it
/// leaves the schema untyped: <c>format</c> without <c>type</c>, and the nullability gone. NSwag
/// then generates <c>object</c> where the property was <c>DateTimeOffset</c>. The type is restored
/// here, as <see cref="EnumSchemaTransformer"/> does for enumerations.
/// </remarks>
internal sealed class DateSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var declared = context.JsonTypeInfo.Type;
        var type = Nullable.GetUnderlyingType(declared) ?? declared;
        if (type != typeof(DateTimeOffset) && type != typeof(DateTime))
        {
            return Task.CompletedTask;
        }

        // Stated as the union, which the 3.0 writer turns into `nullable: true`.
        schema.Type = Nullable.GetUnderlyingType(declared) is null
            ? JsonSchemaType.String
            : JsonSchemaType.String | JsonSchemaType.Null;
        schema.Format = "date-time";

        return Task.CompletedTask;
    }
}
