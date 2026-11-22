using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Makes nullability the contract: a non-nullable member is required, a nullable one is optional.
/// </summary>
/// <remarks>
/// <para>
/// So a model declares <c>string Email</c> or <c>string? Name</c> and says everything: no
/// <c>required</c> modifier, no <c>[Required]</c>. One rule, expressed in the language rather than
/// in decoration, and impossible to contradict: a member cannot be documented optional while the
/// type says otherwise.
/// </para>
/// <para>
/// This is needed even though MVC infers <c>[Required]</c> for non-nullable reference types,
/// because that inference lands in MVC's validation metadata while the OpenAPI schema is built
/// from the serializer's. Without this the constraint would be enforced at runtime and absent from
/// the document.
/// </para>
/// </remarks>
internal sealed class RequiredByNullabilitySchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (schema.Properties is null || schema.Properties.Count == 0)
        {
            return Task.CompletedTask;
        }

        foreach (var property in context.JsonTypeInfo.Properties)
        {
            // IsSetNullable reports the annotation the declaring member carries, which is the
            // signal wanted, including for members whose schema is a reference and so says
            // nothing about nullability itself.
            if (!property.IsSetNullable && schema.Properties.ContainsKey(property.Name))
            {
                (schema.Required ??= new HashSet<string>()).Add(property.Name);
            }
        }

        return Task.CompletedTask;
    }
}
