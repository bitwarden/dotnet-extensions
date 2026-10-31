using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// A marked resource schema is published in the enveloped wire shape: its members under a named
/// <c>attributes</c> schema with their required status, <c>type</c> pinned to its one value, and
/// the marker gone. An unmarked schema is left as it is.
/// </summary>
public sealed class ResourceEnvelopeDocumentTransformerTests
{
    [Fact]
    public async Task TransformAsync_Resource_MovesItsMembersUnderAttributes()
    {
        var name = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Kept with the member." };
        var resource = Resource("widget", ("id", new OpenApiSchema()), ("type", new OpenApiSchema()), ("name", name), ("note", new OpenApiSchema()));
        resource.Required = new HashSet<string> { "id", "name" };
        var document = Document(("Widget", resource));

        await Transform(document);

        Assert.Equal(["attributes", "id", "type"], resource.Properties!.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("WidgetAttributes", Assert.IsType<OpenApiSchemaReference>(resource.Properties["attributes"]).Reference.Id);
        Assert.Equal(["attributes", "id", "type"], resource.Required.Order(StringComparer.Ordinal));

        var attributes = Assert.IsType<OpenApiSchema>(document.Components!.Schemas!["WidgetAttributes"]);
        Assert.Equal(JsonSchemaType.Object, attributes.Type);
        Assert.Equal(["name", "note"], attributes.Properties!.Keys.Order(StringComparer.Ordinal));
        Assert.Same(name, attributes.Properties["name"]);
        Assert.Equal(["name"], attributes.Required!);
    }

    [Fact]
    public async Task TransformAsync_Resource_PinsItsType()
    {
        var type = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null };
        var resource = Resource("api-key", ("id", new OpenApiSchema()), ("type", type));
        var document = Document(("ApiKey", resource));

        await Transform(document);

        Assert.Equal(JsonSchemaType.String, type.Type);
        Assert.Equal(["api-key"], type.Enum!.Select(value => value!.GetValue<string>()));
        Assert.Contains("type", resource.Required!);
    }

    [Fact]
    public async Task TransformAsync_Resource_LosesTheMarker()
    {
        var resource = Resource("widget", ("id", new OpenApiSchema()));
        var document = Document(("Widget", resource));

        await Transform(document);

        Assert.False(resource.Extensions!.ContainsKey(ResourceSchemaTransformer.Marker));
    }

    [Fact]
    public async Task TransformAsync_ResourceWithOnlyIdAndType_GetsNoAttributes()
    {
        var resource = Resource("widget", ("id", new OpenApiSchema()), ("type", new OpenApiSchema()));
        var document = Document(("Widget", resource));

        await Transform(document);

        Assert.Equal(["id", "type"], resource.Properties!.Keys.Order(StringComparer.Ordinal));
        Assert.False(document.Components!.Schemas!.ContainsKey("WidgetAttributes"));
    }

    [Fact]
    public async Task TransformAsync_ResourceWithoutProperties_IsOnlyUnmarked()
    {
        var resource = Resource("widget");
        resource.Properties = null;
        var document = Document(("Widget", resource));

        await Transform(document);

        Assert.Null(resource.Properties);
        Assert.Null(resource.Required);
        Assert.Empty(resource.Extensions!);
    }

    [Fact]
    public async Task TransformAsync_ReferencedType_IsLeftAlone()
    {
        var resource = Resource("widget", ("name", new OpenApiSchema()));
        resource.Properties!["type"] = new OpenApiSchemaReference("Kind", new OpenApiDocument());
        var document = Document(("Widget", resource));

        await Transform(document);

        Assert.IsType<OpenApiSchemaReference>(resource.Properties["type"]);
        Assert.DoesNotContain("type", resource.Required!);
    }

    [Fact]
    public async Task TransformAsync_UnmarkedSchema_IsLeftAlone()
    {
        var schema = new OpenApiSchema
        {
            Properties = new Dictionary<string, IOpenApiSchema> { ["id"] = new OpenApiSchema(), ["name"] = new OpenApiSchema() },
        };
        var document = Document(("Arguments", schema));

        await Transform(document);

        Assert.Equal(["id", "name"], schema.Properties.Keys.Order(StringComparer.Ordinal));
        Assert.Single(document.Components!.Schemas!);
    }

    [Fact]
    public async Task TransformAsync_MarkerThatIsNotAString_IsNotAResource()
    {
        var schema = new OpenApiSchema
        {
            Properties = new Dictionary<string, IOpenApiSchema> { ["name"] = new OpenApiSchema() },
            Extensions = new Dictionary<string, IOpenApiExtension>
            {
                [ResourceSchemaTransformer.Marker] = new JsonNodeExtension(JsonValue.Create(42)),
            },
        };
        var document = Document(("Widget", schema));

        await Transform(document);

        Assert.Equal(["name"], schema.Properties.Keys);
        Assert.True(schema.Extensions.ContainsKey(ResourceSchemaTransformer.Marker));
    }

    [Fact]
    public async Task TransformAsync_NoSchemas_IsLeftAlone()
    {
        var document = new OpenApiDocument();

        await Transform(document);

        Assert.Null(document.Components);
    }

    private static OpenApiSchema Resource(string type, params (string Name, OpenApiSchema Schema)[] properties) => new()
    {
        Type = JsonSchemaType.Object,
        Properties = properties.ToDictionary(property => property.Name, IOpenApiSchema (property) => property.Schema),
        Extensions = new Dictionary<string, IOpenApiExtension>
        {
            [ResourceSchemaTransformer.Marker] = new JsonNodeExtension(JsonValue.Create(type)),
        },
    };

    private static OpenApiDocument Document(params (string Name, OpenApiSchema Schema)[] schemas) => new()
    {
        Components = new OpenApiComponents
        {
            Schemas = schemas.ToDictionary(schema => schema.Name, IOpenApiSchema (schema) => schema.Schema),
        },
    };

    private static Task Transform(OpenApiDocument document) =>
        new ResourceEnvelopeDocumentTransformer().TransformAsync(document, new OpenApiDocumentTransformerContext
        {
            DocumentName = "v1",
            DescriptionGroups = [],
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);
}
