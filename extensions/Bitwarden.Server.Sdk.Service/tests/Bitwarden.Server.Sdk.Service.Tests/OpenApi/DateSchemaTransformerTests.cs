using System.Text.Json;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// A date written through the platform's own converters is published as a <c>date-time</c>
/// string, nullable where the member is, and nothing else is touched.
/// </summary>
public sealed class DateSchemaTransformerTests
{
    [Theory]
    [InlineData(typeof(DateTimeOffset))]
    [InlineData(typeof(DateTime))]
    public async Task TransformAsync_Date_IsADateTimeString(Type type)
    {
        var schema = new OpenApiSchema { Format = "date-time" };

        await Transform(schema, type);

        Assert.Equal(JsonSchemaType.String, schema.Type);
        Assert.Equal("date-time", schema.Format);
    }

    [Theory]
    [InlineData(typeof(DateTimeOffset?))]
    [InlineData(typeof(DateTime?))]
    public async Task TransformAsync_NullableDate_IsAStringOrNull(Type type)
    {
        var schema = new OpenApiSchema();

        await Transform(schema, type);

        Assert.Equal(JsonSchemaType.String | JsonSchemaType.Null, schema.Type);
        Assert.Equal("date-time", schema.Format);
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(DateOnly))]
    [InlineData(typeof(int?))]
    public async Task TransformAsync_NotADate_IsLeftAlone(Type type)
    {
        var schema = new OpenApiSchema();

        await Transform(schema, type);

        Assert.Null(schema.Type);
        Assert.Null(schema.Format);
    }

    private static Task Transform(OpenApiSchema schema, Type type) =>
        new DateSchemaTransformer().TransformAsync(schema, new OpenApiSchemaTransformerContext
        {
            DocumentName = "v1",
            JsonTypeInfo = JsonSerializerOptions.Web.GetTypeInfo(type),
            JsonPropertyInfo = null,
            ParameterDescription = null,
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);
}
