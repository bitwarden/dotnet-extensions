using System.Reflection;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// A handler that returns nothing is published as succeeding with <c>204</c> rather than the
/// <c>200</c> the generator infers, and one that returns a value keeps its success.
/// </summary>
public sealed class StandardResponsesOperationTransformerTests
{
    [Theory]
    [InlineData(nameof(Handlers.Nothing))]
    [InlineData(nameof(Handlers.NothingLater))]
    [InlineData(nameof(Handlers.NothingSoon))]
    public async Task TransformAsync_ReturnsNothing_SucceedsWith204(string handler)
    {
        var operation = new OpenApiOperation { Responses = new OpenApiResponses { ["200"] = new OpenApiResponse() } };

        await Transform(operation, typeof(Handlers).GetMethod(handler));

        Assert.Equal(["204"], operation.Responses.Keys);
        Assert.Equal("The request succeeded. There is no response body.", operation.Responses["204"].Description);
    }

    [Fact]
    public async Task TransformAsync_ReturnsAValue_Keeps200()
    {
        var ok = new OpenApiResponse();
        var operation = new OpenApiOperation { Responses = new OpenApiResponses { ["200"] = ok } };

        await Transform(operation, typeof(Handlers).GetMethod(nameof(Handlers.Something)));

        Assert.Equal(["200"], operation.Responses.Keys);
        Assert.Same(ok, operation.Responses["200"]);
    }

    // Only the inferred 200 is replaced; a success the operation declared otherwise stands.
    [Fact]
    public async Task TransformAsync_ReturnsNothingWithAnotherSuccess_IsLeftAlone()
    {
        var operation = new OpenApiOperation { Responses = new OpenApiResponses { ["202"] = new OpenApiResponse() } };

        await Transform(operation, typeof(Handlers).GetMethod(nameof(Handlers.Nothing)));

        Assert.Equal(["202"], operation.Responses.Keys);
    }

    [Fact]
    public async Task TransformAsync_NoHandlerKnown_Keeps200()
    {
        var operation = new OpenApiOperation { Responses = new OpenApiResponses { ["200"] = new OpenApiResponse() } };

        await Transform(operation, handler: null);

        Assert.Equal(["200"], operation.Responses.Keys);
    }

    [Fact]
    public async Task TransformAsync_NoResponses_GetsAnEmptySet()
    {
        var operation = new OpenApiOperation();

        await Transform(operation, typeof(Handlers).GetMethod(nameof(Handlers.Nothing)));

        Assert.NotNull(operation.Responses);
        Assert.Empty(operation.Responses);
    }

    private static Task Transform(OpenApiOperation operation, MethodInfo? handler) =>
        new StandardResponsesOperationTransformer().TransformAsync(operation, new OpenApiOperationTransformerContext
        {
            DocumentName = "v1",
            Description = new ApiDescription
            {
                ActionDescriptor = new ActionDescriptor { EndpointMetadata = handler is null ? [] : [handler] },
            },
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);

    /// <summary>
    /// Handlers returning each kind of nothing, and something.
    /// </summary>
    public static class Handlers
    {
        /// <summary>
        /// Returns nothing.
        /// </summary>
        public static void Nothing()
        {
        }

        /// <summary>

        /// Returns nothing, later.

        /// </summary>
        public static Task NothingLater() => Task.CompletedTask;

        /// <summary>

        /// Returns nothing, soon.

        /// </summary>
        public static ValueTask NothingSoon() => ValueTask.CompletedTask;

        /// <summary>

        /// Returns a value.

        /// </summary>
        public static Task<string> Something() => Task.FromResult("value");
    }
}
