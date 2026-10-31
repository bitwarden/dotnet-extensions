using System.ComponentModel;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Service.Endpoints;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// The paging parameters an operation declares are published as bounded integers with the
/// default the signature gives, or the one the type stands for, and the cursor as the opaque
/// string a caller passes back, each described by the platform.
/// </summary>
public sealed class PagingDocumentTransformerTests
{
    [Fact]
    public async Task TransformAsync_PageNumber_IsAnIntegerFromOne()
    {
        var operation = await Transform(nameof(Handlers.ListWidgets), new OpenApiParameter { Name = "page[number]", Required = true });

        var number = Assert.Single(operation.Parameters!);
        Assert.Equal(ParameterLocation.Query, number.In);
        Assert.False(number.Required);
        Assert.Equal("Which page to return, from 1.", number.Description);
        Assert.Equal(JsonSchemaType.Integer, number.Schema!.Type);
        Assert.Equal("int32", number.Schema.Format);
        Assert.Equal("1", number.Schema.Minimum);
        Assert.Null(number.Schema.Maximum);
        Assert.Equal(1, number.Schema.Default!.GetValue<int>());
    }

    [Fact]
    public async Task TransformAsync_PageSize_IsBoundedByTheMaximum()
    {
        var operation = await Transform(nameof(Handlers.ListWidgets), new OpenApiParameter { Name = "page[size]" });

        var size = Assert.Single(operation.Parameters!);
        Assert.Equal("1", size.Schema!.Minimum);
        Assert.Equal("200", size.Schema.Maximum);
        Assert.Equal(50, size.Schema.Default!.GetValue<int>());
        Assert.Equal("How many resources the page holds. Values above 200 are treated as 200.", size.Description);
    }

    [Fact]
    public async Task TransformAsync_PageSizeWithDefaultValue_PublishesIt()
    {
        var operation = await Transform(nameof(Handlers.ListSmallPages), new OpenApiParameter { Name = "page[size]" });

        Assert.Equal(25, Assert.Single(operation.Parameters!).Schema!.Default!.GetValue<int>());
    }

    // An int parameter's own default reaches the declared schema, and is kept.
    [Theory]
    [InlineData("page[number]", 3)]
    [InlineData("page[size]", 10)]
    public async Task TransformAsync_DeclaredDefault_IsKept(string name, int declared)
    {
        var parameter = new OpenApiParameter { Name = name, Schema = new OpenApiSchema { Default = JsonValue.Create(declared) } };

        var operation = await Transform(nameof(Handlers.ListWidgets), parameter);

        Assert.Equal(declared, Assert.Single(operation.Parameters!).Schema!.Default!.GetValue<int>());
    }

    [Fact]
    public async Task TransformAsync_DeclaredDefaultThatIsNotANumber_GetsTheTypesOwn()
    {
        var parameter = new OpenApiParameter { Name = "page[number]", Schema = new OpenApiSchema { Default = JsonValue.Create("first") } };

        var operation = await Transform(nameof(Handlers.ListWidgets), parameter);

        Assert.Equal(1, Assert.Single(operation.Parameters!).Schema!.Default!.GetValue<int>());
    }

    [Fact]
    public async Task TransformAsync_PageAfter_IsAnOpaqueCursor()
    {
        var operation = await Transform(nameof(Handlers.ListEvents), new OpenApiParameter { Name = "page[after]", Required = true });

        var after = Assert.Single(operation.Parameters!);
        Assert.False(after.Required);
        Assert.Equal(JsonSchemaType.String, after.Schema!.Type);
        Assert.Contains("meta.cursor", after.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TransformAsync_OtherParameters_AreLeftAlone()
    {
        var sort = new OpenApiParameter { Name = "sort" };

        var operation = await Transform(nameof(Handlers.ListWidgets), sort);

        Assert.Same(sort, Assert.Single(operation.Parameters!));
    }

    [Fact]
    public async Task TransformAsync_NoParametersOrPaths_IsLeftAlone()
    {
        var operation = new OpenApiOperation();

        await Run(Document(operation), nameof(Handlers.ListWidgets));
        await Run(new OpenApiDocument { Paths = null! }, nameof(Handlers.ListWidgets));

        Assert.Null(operation.Parameters);
    }

    private static async Task<OpenApiOperation> Transform(string handler, params OpenApiParameter[] parameters)
    {
        var operation = new OpenApiOperation { Parameters = [.. parameters] };
        await Run(Document(operation), handler);
        return operation;
    }

    private static OpenApiDocument Document(OpenApiOperation operation) => new()
    {
        Paths = new OpenApiPaths
        {
            ["/api/v1/widgets"] = new OpenApiPathItem
            {
                Operations = new Dictionary<HttpMethod, OpenApiOperation> { [HttpMethod.Get] = operation },
            },
        },
    };

    private static Task Run(OpenApiDocument document, string handler)
    {
        var method = typeof(Handlers).GetMethod(handler)!;
        var description = new ApiDescription
        {
            HttpMethod = "GET",
            RelativePath = "api/v1/widgets",
            ActionDescriptor = new ActionDescriptor
            {
                EndpointMetadata = [method],
                Parameters = [.. method.GetParameters().Select(ParameterDescriptor (parameter) => new HandlerParameterDescriptor
                {
                    Name = parameter.Name!,
                    ParameterType = parameter.ParameterType,
                    ParameterInfo = parameter,
                })],
            },
        };

        return new PagingDocumentTransformer().TransformAsync(document, new OpenApiDocumentTransformerContext
        {
            DocumentName = "v1",
            DescriptionGroups = [new ApiDescriptionGroup("v1", [description])],
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Paged reads.
    /// </summary>
    public static class Handlers
    {
        /// <summary>
        /// Offset-paged with the defaults.
        /// </summary>
        public static void ListWidgets(PageNumber pageNumber = default, PageSize pageSize = default)
        {
        }

        /// <summary>

        /// Offset-paged with a smaller page.

        /// </summary>
        public static void ListSmallPages(PageNumber pageNumber = default, [DefaultValue(25)] PageSize pageSize = default)
        {
        }

        /// <summary>

        /// Cursor-paged.

        /// </summary>
        public static void ListEvents(PageSize pageSize = default, PageAfter pageAfter = default)
        {
        }
    }
}
