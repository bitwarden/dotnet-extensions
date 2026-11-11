using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bitwarden.Server.Sdk.Service.Features;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.Features;

/// <summary>
/// The config resource's <c>featureFlags</c> is published as a map (<c>additionalProperties: {}</c>); no other
/// member, and no schema already saying what its members are, is touched.
/// </summary>
public sealed class FeatureFlagsSchemaTransformerTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    [Fact]
    public async Task ConfigsFeatureFlags_BecomeAMap()
    {
        var schema = new OpenApiSchema();

        await Transform(schema, Property(typeof(Config), "featureFlags"));

        Assert.NotNull(schema.AdditionalProperties);
    }

    // A schema that already says what its members are keeps it.
    [Fact]
    public async Task ConfigsFeatureFlags_AlreadyAMap_AreKept()
    {
        var members = new OpenApiSchema { Type = JsonSchemaType.Boolean };
        var schema = new OpenApiSchema { AdditionalProperties = members };

        await Transform(schema, Property(typeof(Config), "featureFlags"));

        Assert.Same(members, schema.AdditionalProperties);
    }

    // Another member of the config resource, a member of the same name elsewhere, or a schema for a whole type: left
    // as published.
    [Theory]
    [InlineData(typeof(Config), "settings")]
    [InlineData(typeof(Elsewhere), "featureFlags")]
    [InlineData(null, null)]
    public async Task AnythingElse_IsLeftAlone(Type? declaring, string? name)
    {
        var schema = new OpenApiSchema();

        await Transform(schema, declaring is null ? null : Property(declaring, name!));

        Assert.Null(schema.AdditionalProperties);
    }

    private static JsonPropertyInfo Property(Type type, string name) =>
        Options.GetTypeInfo(type).Properties.Single(property => property.Name == name);

    private static Task Transform(OpenApiSchema schema, JsonPropertyInfo? property) =>
        new FeatureFlagsSchemaTransformer().TransformAsync(
            schema,
            new OpenApiSchemaTransformerContext
            {
                DocumentName = "v1",
                JsonTypeInfo = property is null ? Options.GetTypeInfo(typeof(Config)) : Options.GetTypeInfo(property.PropertyType),
                JsonPropertyInfo = property,
                ParameterDescription = null,
                ApplicationServices = new ServiceCollection().BuildServiceProvider(),
            },
            TestContext.Current.CancellationToken);

    /// <summary>
    /// Another type with a member of the same name.
    /// </summary>
    public sealed record Elsewhere(IReadOnlyDictionary<string, object> FeatureFlags);
}
