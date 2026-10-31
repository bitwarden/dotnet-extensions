using System.Text.Json;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// A resource's schema is marked with its resource type and otherwise left flat, so the schema
/// transformers after it still find its members where the record declares them.
/// </summary>
public sealed class ResourceSchemaTransformerTests
{
    /// <summary>
    /// A resource named by its type.
    /// </summary>
    public sealed record SsoConfig : Resource
    {
        /// <summary>
        /// Free-form.
        /// </summary>
        public string? Label { get; init; }
    }

    /// <summary>
    /// A resource naming its type outright.
    /// </summary>
    [ResourceType("widget-thing")]
    public sealed record Renamed : Resource;

    [Theory]
    [InlineData(typeof(SsoConfig), "sso-config")]
    [InlineData(typeof(Renamed), "widget-thing")]
    public async Task TransformAsync_Resource_IsMarkedWithItsType(Type type, string expected)
    {
        var schema = new OpenApiSchema
        {
            Properties = new Dictionary<string, IOpenApiSchema> { ["label"] = new OpenApiSchema() },
        };

        await Transform(schema, type);

        var marker = Assert.IsType<JsonNodeExtension>(schema.Extensions![ResourceSchemaTransformer.Marker]);
        Assert.Equal(expected, marker.Node.GetValue<string>());
        Assert.Equal(["label"], schema.Properties.Keys);
    }

    [Fact]
    public async Task TransformAsync_ExistingExtensions_AreKept()
    {
        var schema = new OpenApiSchema
        {
            Extensions = new Dictionary<string, IOpenApiExtension> { ["x-other"] = new JsonNodeExtension("kept") },
        };

        await Transform(schema, typeof(SsoConfig));

        Assert.Equal([ResourceSchemaTransformer.Marker, "x-other"], schema.Extensions.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TransformAsync_NotAResource_IsNotMarked()
    {
        var schema = new OpenApiSchema();

        await Transform(schema, typeof(string));

        Assert.Null(schema.Extensions);
    }

    private static Task Transform(OpenApiSchema schema, Type type) =>
        new ResourceSchemaTransformer().TransformAsync(schema, new OpenApiSchemaTransformerContext
        {
            DocumentName = "v1",
            JsonTypeInfo = JsonSerializerOptions.Web.GetTypeInfo(type),
            JsonPropertyInfo = null,
            ParameterDescription = null,
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);
}
