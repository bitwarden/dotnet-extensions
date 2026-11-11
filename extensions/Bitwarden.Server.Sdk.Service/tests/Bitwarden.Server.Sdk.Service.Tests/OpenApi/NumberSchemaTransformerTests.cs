using System.Text.Json;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// A number .NET types as a number or a string is published as a number: the string spelling
/// and the pattern policing it are dropped, and the format and nullability kept.
/// </summary>
public sealed class NumberSchemaTransformerTests
{
    [Theory]
    [InlineData(JsonSchemaType.Integer | JsonSchemaType.String, JsonSchemaType.Integer)]
    [InlineData(JsonSchemaType.Number | JsonSchemaType.String, JsonSchemaType.Number)]
    [InlineData(JsonSchemaType.Integer | JsonSchemaType.String | JsonSchemaType.Null, JsonSchemaType.Integer | JsonSchemaType.Null)]
    public async Task TransformAsync_NumberOrString_IsANumber(JsonSchemaType declared, JsonSchemaType expected)
    {
        var schema = new OpenApiSchema { Type = declared, Format = "int32", Pattern = "^-?(?:0|[1-9]\\d*)$" };

        await Transform(schema);

        Assert.Equal(expected, schema.Type);
        Assert.Null(schema.Pattern);
        Assert.Equal("int32", schema.Format);
    }

    [Theory]
    [InlineData(JsonSchemaType.String)]
    [InlineData(JsonSchemaType.Integer)]
    [InlineData(JsonSchemaType.Number | JsonSchemaType.Null)]
    public async Task TransformAsync_NotTheUnion_IsLeftAlone(JsonSchemaType declared)
    {
        var schema = new OpenApiSchema { Type = declared, Pattern = "^x$" };

        await Transform(schema);

        Assert.Equal(declared, schema.Type);
        Assert.Equal("^x$", schema.Pattern);
    }

    [Fact]
    public async Task TransformAsync_Untyped_IsLeftAlone()
    {
        var schema = new OpenApiSchema { Pattern = "^x$" };

        await Transform(schema);

        Assert.Null(schema.Type);
        Assert.Equal("^x$", schema.Pattern);
    }

    private static Task Transform(OpenApiSchema schema) =>
        new NumberSchemaTransformer().TransformAsync(schema, new OpenApiSchemaTransformerContext
        {
            DocumentName = "v1",
            JsonTypeInfo = JsonSerializerOptions.Web.GetTypeInfo(typeof(int)),
            JsonPropertyInfo = null,
            ParameterDescription = null,
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);
}
