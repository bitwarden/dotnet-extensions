using Bitwarden.Server.Sdk.Service.JsonApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Documents every <see cref="ReadOnlyAttribute"/> member as <c>readOnly</c>.
/// </summary>
/// <remarks>
/// Set at the schema stage so the flag travels with the property when the envelope moves it under
/// <c>attributes</c>.
/// </remarks>
internal sealed class ReadOnlySchemaTransformer : IOpenApiSchemaTransformer
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
            var readOnly = property.AttributeProvider?
                .GetCustomAttributes(typeof(ReadOnlyAttribute), inherit: true)
                .Length > 0;

            if (readOnly
                && schema.Properties.TryGetValue(property.Name, out var member)
                && member is OpenApiSchema concrete)
            {
                concrete.ReadOnly = true;
            }
        }

        return Task.CompletedTask;
    }
}
