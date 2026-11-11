using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Describes a number as a number.
/// </summary>
/// <remarks>
/// <para>
/// .NET types an <c>int</c> as <c>integer</c> <em>or</em> <c>string</c>, with a pattern to
/// police the string, because its binders will read either. Serialized as OpenAPI 3.0, which
/// has no type unions, that becomes an <c>anyOf</c> of unrelated primitives, and no generator
/// can make a number out of one: NSwag emits an empty class named after the property, so a
/// nullable <c>Seats</c> on a resource reaches callers as <c>public Seats Seats</c> rather than
/// <c>int?</c>.
/// </para>
/// <para>
/// True of the binder, wrong about the contract. What belongs in a JSON body is a number, and
/// the schema says so, keeping the format, the bounds and the nullability, dropping only
/// the alternative spelling and the pattern that policed it.
/// </para>
/// <para>
/// Done here, on the type union, rather than on the <c>anyOf</c> a reader of the document sees:
/// the union is what exists while transformers run, and the <c>anyOf</c> is what the 3.0
/// serializer makes of it afterwards, far too late to intervene.
/// </para>
/// </remarks>
internal sealed class NumberSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (schema.Type is not { } type)
        {
            return Task.CompletedTask;
        }

        var numeric = type & (JsonSchemaType.Integer | JsonSchemaType.Number);

        // Only the union this is about. A schema that is genuinely a string, or genuinely a
        // number already, has nothing to drop.
        if (numeric == 0 || !type.HasFlag(JsonSchemaType.String))
        {
            return Task.CompletedTask;
        }

        // Null survives: a nullable number is still nullable.
        schema.Type = type & ~JsonSchemaType.String;

        // Policed the string spelling, which is gone.
        schema.Pattern = null;

        return Task.CompletedTask;
    }
}
