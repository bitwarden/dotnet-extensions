using System.Reflection;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// Every bulk operation publishes the optional <c>atomic</c> query parameter, defaulting to
/// all-or-nothing where it is offered and limited to the one mode an operation supports, and no
/// other operation gets it.
/// </summary>
public sealed class BulkAtomicityOperationTransformerTests
{
    [Theory]
    [InlineData(nameof(Handlers.UpdateWidgets))]
    [InlineData(nameof(Handlers.DeleteWidgets))]
    [InlineData(nameof(Handlers.CreateWidgets))]
    [InlineData(nameof(Handlers.ReplaceWidgets))]
    [InlineData(nameof(Handlers.Archive))]
    public async Task TransformAsync_EveryBulkOperation_PublishesAtomic(string handler)
    {
        var operation = new OpenApiOperation();

        await Transform(operation, handler);

        var atomic = Assert.Single(operation.Parameters!);
        Assert.Equal("atomic", atomic.Name);
        Assert.Equal(ParameterLocation.Query, atomic.In);
        Assert.False(atomic.Required);
        Assert.Equal(JsonSchemaType.Boolean, atomic.Schema!.Type);
    }

    [Fact]
    public async Task TransformAsync_BothModes_DefaultsToAllOrNothing()
    {
        var operation = new OpenApiOperation();

        await Transform(operation, nameof(Handlers.UpdateWidgets));

        var atomic = Assert.Single(operation.Parameters!);
        Assert.True(atomic.Schema!.Default!.GetValue<bool>());
        Assert.Null(atomic.Schema.Enum);
        Assert.StartsWith("Whether every resource must succeed", atomic.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TransformAsync_AllOrNothingOnly_PermitsOnlyTrue()
    {
        var operation = new OpenApiOperation();

        await Transform(operation, nameof(Handlers.CreateWidgets));

        var atomic = Assert.Single(operation.Parameters!);
        Assert.True(atomic.Schema!.Default!.GetValue<bool>());
        Assert.True(Assert.Single(atomic.Schema.Enum!)!.GetValue<bool>());
        Assert.Contains("refuses 'false' with 422", atomic.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TransformAsync_BestEffortOnly_PermitsOnlyFalse()
    {
        var operation = new OpenApiOperation();

        await Transform(operation, nameof(Handlers.DeleteWidgets));

        var atomic = Assert.Single(operation.Parameters!);
        Assert.False(atomic.Schema!.Default!.GetValue<bool>());
        Assert.False(Assert.Single(atomic.Schema.Enum!)!.GetValue<bool>());
        Assert.Contains("refuses 'true' with 422", atomic.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TransformAsync_DeclaredParameters_AreKept()
    {
        var operation = new OpenApiOperation { Parameters = [new OpenApiParameter { Name = "dryRun" }] };

        await Transform(operation, nameof(Handlers.ReplaceWidgets));

        Assert.Equal(["dryRun", "atomic"], operation.Parameters.Select(parameter => parameter.Name));
    }

    [Fact]
    public async Task TransformAsync_NotABulkOperation_GetsNothing()
    {
        var operation = new OpenApiOperation();

        await Transform(operation, nameof(Handlers.GetWidget));

        Assert.Null(operation.Parameters);
    }

    [Fact]
    public async Task TransformAsync_NoHandlerKnown_GetsNothing()
    {
        var operation = new OpenApiOperation();

        await Transform(operation, handler: null);

        Assert.Null(operation.Parameters);
    }

    [Fact]
    public async Task TransformAsync_NoOperationOrContext_Throws()
    {
        var transformer = new BulkAtomicityOperationTransformer();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            transformer.TransformAsync(null!, Context(handler: null), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            transformer.TransformAsync(new OpenApiOperation(), null!, TestContext.Current.CancellationToken));
    }

    private static Task Transform(OpenApiOperation operation, string? handler) =>
        new BulkAtomicityOperationTransformer().TransformAsync(
            operation,
            Context(handler is null ? null : typeof(Handlers).GetMethod(handler)),
            TestContext.Current.CancellationToken);

    private static OpenApiOperationTransformerContext Context(MethodInfo? handler) => new()
    {
        DocumentName = "v1",
        Description = new ApiDescription
        {
            ActionDescriptor = new ActionDescriptor { EndpointMetadata = handler is null ? [] : [handler] },
        },
        ApplicationServices = new ServiceCollection().BuildServiceProvider(),
    };

    /// <summary>
    /// One handler per bulk attribute, and one that is not bulk.
    /// </summary>
    public static class Handlers
    {
        private const string Collection = "api/v1/widgets";

        /// <summary>

        /// Both modes.

        /// </summary>
        [StandardBulkUpdate(Collection, Supports = Atomicity.AllOrNothing | Atomicity.BestEffort)]
        public static void UpdateWidgets()
        {
        }

        /// <summary>

        /// Best-effort only.

        /// </summary>
        [StandardBulkDelete(Collection, Supports = Atomicity.BestEffort)]
        public static void DeleteWidgets()
        {
        }

        /// <summary>

        /// All-or-nothing only, the default.

        /// </summary>
        [StandardBulkCreate(Collection)]
        public static void CreateWidgets()
        {
        }

        /// <summary>

        /// All-or-nothing only, the default.

        /// </summary>
        [StandardBulkReplace(Collection)]
        public static void ReplaceWidgets()
        {
        }

        /// <summary>

        /// A bulk action.

        /// </summary>
        [StandardBulkAction(Collection)]
        public static void Archive()
        {
        }

        /// <summary>

        /// Not bulk.

        /// </summary>
        [StandardReadOne]
        public static void GetWidget()
        {
        }
    }
}
