using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Features;

/// <summary>
/// Publishes the config resource's <c>featureFlags</c> as a map, rather than as a bare
/// <c>type: object</c>.
/// </summary>
/// <remarks>
/// <para>
/// .NET publishes a dictionary of <see cref="object"/> values as <c>type: object</c> and nothing
/// more. The specification reads that as "any members", but generators do not agree on it: some
/// produce a free-form value, some an empty type with nowhere to put what arrives. Stating
/// <c>additionalProperties: {}</c> makes it a map in every generator.
/// </para>
/// <para>
/// Limited to this one member on purpose. The same holds for every such dictionary, but other
/// services already publish theirs the other way, and changing all of them changes contracts
/// their generated clients were built from.
/// </para>
/// </remarks>
internal sealed class FeatureFlagsSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (context.JsonPropertyInfo is { } property
            && property.DeclaringType == typeof(Config)
            && property.Name == "featureFlags"
            && schema.AdditionalProperties is null)
        {
            schema.AdditionalProperties = new OpenApiSchema();
        }

        return Task.CompletedTask;
    }
}
