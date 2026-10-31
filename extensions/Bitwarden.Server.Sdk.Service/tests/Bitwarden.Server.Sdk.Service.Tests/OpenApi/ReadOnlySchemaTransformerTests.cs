using System.Text.Json;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// A member declared <see cref="ReadOnlyAttribute"/> is published <c>readOnly</c>, under
/// its name on the wire, and no other member is.
/// </summary>
public sealed class ReadOnlySchemaTransformerTests
{
    /// <summary>
    /// One member the server sets and one the caller does.
    /// </summary>
    public sealed class Gadget
    {
        /// <summary>
        /// Set by the server.
        /// </summary>
        [ReadOnly]
        public string? SerialNumber { get; init; }

        /// <summary>

        /// Set by the caller.

        /// </summary>
        public string? Label { get; init; }
    }

    [Fact]
    public async Task TransformAsync_ReadOnlyMember_IsReadOnly()
    {
        var schema = Schema("serialNumber", "label");

        await Transform(schema, typeof(Gadget));

        Assert.True(((OpenApiSchema)schema.Properties!["serialNumber"]).ReadOnly);
        Assert.False(((OpenApiSchema)schema.Properties!["label"]).ReadOnly);
    }

    [Fact]
    public async Task TransformAsync_ReadOnlyMemberMissingFromTheSchema_IsNotAdded()
    {
        var schema = Schema("label");

        await Transform(schema, typeof(Gadget));

        Assert.Equal(["label"], schema.Properties!.Keys);
    }

    [Fact]
    public async Task TransformAsync_ReadOnlyMemberAsAReference_IsLeftAlone()
    {
        var schema = Schema("label");
        schema.Properties!["serialNumber"] = new OpenApiSchemaReference("Serial", new OpenApiDocument());

        await Transform(schema, typeof(Gadget));

        Assert.IsType<OpenApiSchemaReference>(schema.Properties["serialNumber"]);
    }

    [Fact]
    public async Task TransformAsync_NoProperties_IsLeftAlone()
    {
        var schema = new OpenApiSchema();

        await Transform(schema, typeof(Gadget));

        Assert.Null(schema.Properties);
    }

    private static OpenApiSchema Schema(params string[] properties) => new()
    {
        Type = JsonSchemaType.Object,
        Properties = properties.ToDictionary(
            name => name,
            IOpenApiSchema (_) => new OpenApiSchema { Type = JsonSchemaType.String }),
    };

    private static Task Transform(OpenApiSchema schema, Type type) =>
        new ReadOnlySchemaTransformer().TransformAsync(schema, new OpenApiSchemaTransformerContext
        {
            DocumentName = "v1",
            JsonTypeInfo = JsonSerializerOptions.Web.GetTypeInfo(type),
            JsonPropertyInfo = null,
            ParameterDescription = null,
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);
}
