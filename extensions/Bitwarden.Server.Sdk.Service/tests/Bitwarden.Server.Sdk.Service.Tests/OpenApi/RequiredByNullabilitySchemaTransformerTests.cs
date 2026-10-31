using System.Text.Json;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// Nullability is the contract: a non-nullable member is published required and a nullable one
/// optional, with no attribute saying so.
/// </summary>
public sealed class RequiredByNullabilitySchemaTransformerTests
{
    /// <summary>
    /// A member of each kind.
    /// </summary>
    public sealed class Contact
    {
        /// <summary>
        /// Non-nullable reference.
        /// </summary>
        public string Email { get; init; } = null!;

        /// <summary>

        /// Nullable reference.

        /// </summary>
        public string? Nickname { get; init; }

        /// <summary>

        /// Non-nullable value.

        /// </summary>
        public int Visits { get; init; }

        /// <summary>

        /// Nullable value.

        /// </summary>
        public int? Rating { get; init; }
    }

    [Fact]
    public async Task TransformAsync_NonNullableMembers_AreRequired()
    {
        var schema = Schema("email", "nickname", "visits", "rating");

        await Transform(schema);

        Assert.Equal(["email", "visits"], schema.Required!.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TransformAsync_AlreadyRequired_IsAddedTo()
    {
        var schema = Schema("email");
        schema.Required = new HashSet<string> { "other" };

        await Transform(schema);

        Assert.Equal(["email", "other"], schema.Required.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TransformAsync_MemberMissingFromTheSchema_IsNotRequired()
    {
        var schema = Schema("nickname");

        await Transform(schema);

        Assert.Null(schema.Required);
    }

    [Fact]
    public async Task TransformAsync_NoProperties_IsLeftAlone()
    {
        var schema = new OpenApiSchema { Properties = new Dictionary<string, IOpenApiSchema>() };

        await Transform(schema);

        Assert.Null(schema.Required);
    }

    private static OpenApiSchema Schema(params string[] properties) => new()
    {
        Type = JsonSchemaType.Object,
        Properties = properties.ToDictionary(
            name => name,
            IOpenApiSchema (_) => new OpenApiSchema { Type = JsonSchemaType.String }),
    };

    private static Task Transform(OpenApiSchema schema) =>
        new RequiredByNullabilitySchemaTransformer().TransformAsync(schema, new OpenApiSchemaTransformerContext
        {
            DocumentName = "v1",
            JsonTypeInfo = JsonSerializerOptions.Web.GetTypeInfo(typeof(Contact)),
            JsonPropertyInfo = null,
            ParameterDescription = null,
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);
}
