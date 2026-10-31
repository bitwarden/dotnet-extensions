using System.Text.Json;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// A <see cref="decimal"/> is published in the <c>decimal</c> format a generator reads as exact,
/// rather than the <c>double</c> .NET writes, and a real double keeps its format.
/// </summary>
public sealed class DecimalSchemaTransformerTests
{
    [Theory]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(decimal?))]
    public async Task TransformAsync_DecimalPublishedAsDouble_IsADecimal(Type type)
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.Number, Format = "double" };

        await Transform(schema, type);

        Assert.Equal("decimal", schema.Format);
        Assert.Equal(JsonSchemaType.Number, schema.Type);
    }

    [Theory]
    [InlineData(typeof(double))]
    [InlineData(typeof(double?))]
    [InlineData(typeof(float))]
    public async Task TransformAsync_ADouble_KeepsItsFormat(Type type)
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.Number, Format = "double" };

        await Transform(schema, type);

        Assert.Equal("double", schema.Format);
    }

    [Fact]
    public async Task TransformAsync_DecimalInAnotherFormat_IsLeftAlone()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.String };

        await Transform(schema, typeof(decimal));

        Assert.Null(schema.Format);
    }

    [Fact]
    public async Task TransformAsync_NoContext_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new DecimalSchemaTransformer().TransformAsync(new OpenApiSchema(), null!, TestContext.Current.CancellationToken));
    }

    private static Task Transform(OpenApiSchema schema, Type type) =>
        new DecimalSchemaTransformer().TransformAsync(schema, new OpenApiSchemaTransformerContext
        {
            DocumentName = "v1",
            JsonTypeInfo = JsonSerializerOptions.Web.GetTypeInfo(type),
            JsonPropertyInfo = null,
            ParameterDescription = null,
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);
}
