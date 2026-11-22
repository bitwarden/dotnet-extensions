using System.Text.Json;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// An enumeration is published as the string it is on the wire, with the values in the naming
/// policy's spelling rather than the CLR member names, nullable where the member is.
/// </summary>
public sealed class EnumSchemaTransformerTests
{
    /// <summary>
    /// Members whose CLR names differ from their wire names.
    /// </summary>
    public enum Shade
    {
        /// <summary>
        /// Not a real colour.
        /// </summary>
        DeepRed,

        /// <summary>

        /// Not a real colour either.

        /// </summary>
        Amber,
    }

    [Fact]
    public async Task TransformAsync_Enum_IsAStringOfTheWireValues()
    {
        // As the generator leaves it: the CLR names, and no type.
        var schema = new OpenApiSchema { Enum = [JsonValue.Create("DeepRed"), JsonValue.Create("Amber")] };

        await Transform(schema, typeof(Shade));

        Assert.Equal(JsonSchemaType.String, schema.Type);
        Assert.Equal(["DEEP_RED", "AMBER"], schema.Enum!.Select(value => value!.GetValue<string>()));
    }

    [Fact]
    public async Task TransformAsync_NullableEnum_IsAStringOrNull()
    {
        var schema = new OpenApiSchema();

        await Transform(schema, typeof(Shade?));

        Assert.Equal(JsonSchemaType.String | JsonSchemaType.Null, schema.Type);
        Assert.Equal(["DEEP_RED", "AMBER"], schema.Enum!.Select(value => value!.GetValue<string>()));
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(int?))]
    public async Task TransformAsync_NotAnEnum_IsLeftAlone(Type type)
    {
        var schema = new OpenApiSchema();

        await Transform(schema, type);

        Assert.Null(schema.Type);
        Assert.Null(schema.Enum);
    }

    private static Task Transform(OpenApiSchema schema, Type type) =>
        new EnumSchemaTransformer().TransformAsync(schema, new OpenApiSchemaTransformerContext
        {
            DocumentName = "v1",
            JsonTypeInfo = JsonSerializerOptions.Web.GetTypeInfo(type),
            JsonPropertyInfo = null,
            ParameterDescription = null,
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);
}
