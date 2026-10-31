using System.Text.Json;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// A resource's free-form strings are published with the length the platform enforces, an
/// encrypted member with the bound it declares instead, and nothing already bounded by a length,
/// a format or a list of values is given another.
/// </summary>
public sealed class DefaultStringLengthSchemaTransformerTests
{
    /// <summary>
    /// A resource with a member of every kind the transformer tells apart.
    /// </summary>
    public sealed record Gadget : Resource
    {
        /// <summary>
        /// Free-form.
        /// </summary>
        public string Name { get; init; } = null!;

        /// <summary>

        /// Ciphertext, with its own bound.

        /// </summary>
        [Encrypted(MaxLength = 50)]
        public string? Secret { get; init; }
    }

    /// <summary>
    /// A resource declaring an encrypted member with no bound.
    /// </summary>
    public sealed record Unbounded : Resource
    {
        /// <summary>
        /// Ciphertext, unbounded.
        /// </summary>
        [Encrypted]
        public string? Secret { get; init; }
    }

    /// <summary>
    /// Not a resource.
    /// </summary>
    public sealed class Arguments
    {
        /// <summary>
        /// Free-form.
        /// </summary>
        public string? Reason { get; init; }
    }

    [Fact]
    public async Task TransformAsync_FreeFormString_GetsTheDefaultLength()
    {
        var schema = Schema(("name", new OpenApiSchema { Type = JsonSchemaType.String }));

        await Transform(schema, typeof(Gadget));

        Assert.Equal(255, Property(schema, "name").MaxLength);
    }

    [Fact]
    public async Task TransformAsync_NullableString_GetsTheDefaultLength()
    {
        var schema = Schema(("note", new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null }));

        await Transform(schema, typeof(Gadget));

        Assert.Equal(255, Property(schema, "note").MaxLength);
    }

    [Fact]
    public async Task TransformAsync_EncryptedMember_GetsItsDeclaredBound()
    {
        var schema = Schema(("secret", new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null }));

        await Transform(schema, typeof(Gadget));

        Assert.Equal(50, Property(schema, "secret").MaxLength);
    }

    [Fact]
    public async Task TransformAsync_EncryptedMemberWithNoBound_Refuses()
    {
        var schema = Schema(("secret", new OpenApiSchema { Type = JsonSchemaType.String }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Transform(schema, typeof(Unbounded)));
    }

    [Fact]
    public async Task TransformAsync_AlreadyBounded_KeepsItsLength()
    {
        var schema = Schema(("name", new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = 10 }));

        await Transform(schema, typeof(Gadget));

        Assert.Equal(10, Property(schema, "name").MaxLength);
    }

    [Fact]
    public async Task TransformAsync_FormattedOrEnumerated_GetsNoLength()
    {
        var schema = Schema(
            ("createdAt", new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time" }),
            ("kind", new OpenApiSchema { Type = JsonSchemaType.String, Enum = [JsonValue.Create("A")] }),
            ("count", new OpenApiSchema { Type = JsonSchemaType.Integer }));

        await Transform(schema, typeof(Gadget));

        Assert.Null(Property(schema, "createdAt").MaxLength);
        Assert.Null(Property(schema, "kind").MaxLength);
        Assert.Null(Property(schema, "count").MaxLength);
    }

    [Fact]
    public async Task TransformAsync_EmptyEnumeration_GetsTheDefaultLength()
    {
        var schema = Schema(("name", new OpenApiSchema { Type = JsonSchemaType.String, Enum = [] }));

        await Transform(schema, typeof(Gadget));

        Assert.Equal(255, Property(schema, "name").MaxLength);
    }

    [Fact]
    public async Task TransformAsync_IdAndType_GetNoLength()
    {
        var schema = Schema(
            ("id", new OpenApiSchema { Type = JsonSchemaType.String }),
            ("type", new OpenApiSchema { Type = JsonSchemaType.String }));

        await Transform(schema, typeof(Gadget));

        Assert.Null(Property(schema, "id").MaxLength);
        Assert.Null(Property(schema, "type").MaxLength);
    }

    [Fact]
    public async Task TransformAsync_ReferencedMember_IsLeftAlone()
    {
        var schema = Schema();
        schema.Properties!["name"] = new OpenApiSchemaReference("Name", new OpenApiDocument());

        await Transform(schema, typeof(Gadget));

        Assert.IsType<OpenApiSchemaReference>(schema.Properties["name"]);
    }

    [Fact]
    public async Task TransformAsync_NotAResource_IsLeftAlone()
    {
        var schema = Schema(("reason", new OpenApiSchema { Type = JsonSchemaType.String }));

        await Transform(schema, typeof(Arguments));

        Assert.Null(Property(schema, "reason").MaxLength);
    }

    [Fact]
    public async Task TransformAsync_ResourceWithoutProperties_IsLeftAlone()
    {
        var schema = new OpenApiSchema();

        await Transform(schema, typeof(Gadget));

        Assert.Null(schema.Properties);
    }

    private static OpenApiSchema Schema(params (string Name, OpenApiSchema Schema)[] properties) => new()
    {
        Type = JsonSchemaType.Object,
        Properties = properties.ToDictionary(property => property.Name, IOpenApiSchema (property) => property.Schema),
    };

    private static OpenApiSchema Property(OpenApiSchema schema, string name) => (OpenApiSchema)schema.Properties![name];

    private static Task Transform(OpenApiSchema schema, Type type) =>
        new DefaultStringLengthSchemaTransformer().TransformAsync(schema, new OpenApiSchemaTransformerContext
        {
            DocumentName = "v1",
            JsonTypeInfo = JsonSerializerOptions.Web.GetTypeInfo(type),
            JsonPropertyInfo = null,
            ParameterDescription = null,
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);
}
