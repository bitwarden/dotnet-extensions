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
/// A filter is published as <c>filter[name]</c> with the schema of what a caller sends: its
/// element type, an array for a list, and a patterned string for a range, keeping the author's
/// description and whether it is required.
/// </summary>
public sealed class FilteringDocumentTransformerTests
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

    [Theory]
    [InlineData(nameof(Handlers.ByGuid), JsonSchemaType.String, "uuid")]
    [InlineData(nameof(Handlers.ByDate), JsonSchemaType.String, "date-time")]
    [InlineData(nameof(Handlers.ByLocalDate), JsonSchemaType.String, "date-time")]
    [InlineData(nameof(Handlers.ByFlag), JsonSchemaType.Boolean, null)]
    [InlineData(nameof(Handlers.ByCount), JsonSchemaType.Integer, "int32")]
    [InlineData(nameof(Handlers.ByTotal), JsonSchemaType.Integer, "int64")]
    [InlineData(nameof(Handlers.ByName), JsonSchemaType.String, null)]
    public async Task TransformAsync_Filter_IsPublishedAsItsElement(string handler, JsonSchemaType type, string? format)
    {
        var operation = await Transform(handler, new OpenApiParameter { Name = "value", Description = "Only these.", Required = true });

        var filter = Assert.Single(operation.Parameters!);
        Assert.Equal("filter[value]", filter.Name);
        Assert.Equal(ParameterLocation.Query, filter.In);
        Assert.True(filter.Required);
        Assert.Equal("Only these.", filter.Description);
        Assert.Equal(type, filter.Schema!.Type);
        Assert.Equal(format, filter.Schema.Format);
    }

    [Fact]
    public async Task TransformAsync_EnumFilter_ListsTheWireValues()
    {
        var operation = await Transform(nameof(Handlers.ByShade), new OpenApiParameter { Name = "value" });

        var filter = Assert.Single(operation.Parameters!);
        Assert.Equal(JsonSchemaType.String, filter.Schema!.Type);
        Assert.Equal(["DEEP_RED", "AMBER"], filter.Schema.Enum!.Select(value => value!.GetValue<string>()));
    }

    [Theory]
    [InlineData("Only these.", "Only these. May be repeated, comma-delimited, or both.")]
    [InlineData(null, "May be repeated, comma-delimited, or both.")]
    public async Task TransformAsync_ListFilter_IsAnArraySayingCommasWork(string? authored, string expected)
    {
        var operation = await Transform(nameof(Handlers.ByGuids), new OpenApiParameter { Name = "value", Description = authored });

        var filter = Assert.Single(operation.Parameters!);
        Assert.Equal(JsonSchemaType.Array, filter.Schema!.Type);
        Assert.Equal("uuid", filter.Schema.Items!.Format);
        Assert.Equal(expected, filter.Description);
    }

    [Theory]
    [InlineData(nameof(Handlers.WithinDates), "Each bound is a date-time")]
    [InlineData(nameof(Handlers.WithinCounts), "Each bound is a int32")]
    [InlineData(nameof(Handlers.WithinFlags), "Each bound is a boolean")]
    public async Task TransformAsync_RangeFilter_IsAPatternedStringNamingItsBounds(string handler, string bound)
    {
        var operation = await Transform(handler, new OpenApiParameter { Name = "value" });

        var filter = Assert.Single(operation.Parameters!);
        Assert.Equal("filter[value]", filter.Name);
        Assert.Equal(JsonSchemaType.String, filter.Schema!.Type);
        Assert.Equal(@"^[\[(].+,.+[\])]$", filter.Schema.Pattern);
        Assert.StartsWith("A range: ", filter.Description, StringComparison.Ordinal);
        Assert.Contains(bound, filter.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TransformAsync_RangeFilter_KeepsTheAuthorsDescriptionFirst()
    {
        var operation = await Transform(nameof(Handlers.WithinDates), new OpenApiParameter { Name = "value", Description = "Created within." });

        Assert.StartsWith("Created within. A range: ", Assert.Single(operation.Parameters!).Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TransformAsync_OtherParameters_AreLeftAlone()
    {
        var page = new OpenApiParameter { Name = "page[number]" };

        var operation = await Transform(nameof(Handlers.ByGuid), page, new OpenApiParameter { Name = "value" });

        Assert.Same(page, operation.Parameters![0]);
        Assert.Equal("filter[value]", operation.Parameters[1].Name);
    }

    [Fact]
    public async Task TransformAsync_NoFilters_IsLeftAlone()
    {
        var value = new OpenApiParameter { Name = "value" };

        var operation = await Transform(nameof(Handlers.Plain), value);

        Assert.Same(value, Assert.Single(operation.Parameters!));
    }

    [Fact]
    public async Task TransformAsync_NoParameters_IsLeftAlone()
    {
        var operation = await Transform(nameof(Handlers.ByGuid));
        operation.Parameters = null;

        await Run(Document(operation), nameof(Handlers.ByGuid), "api/v1/widgets");

        Assert.Null(operation.Parameters);
    }

    [Fact]
    public async Task TransformAsync_OperationNotInTheDocument_IsSkipped()
    {
        var operation = new OpenApiOperation { Parameters = [new OpenApiParameter { Name = "value" }] };

        await Run(Document(operation), nameof(Handlers.ByGuid), "api/v1/elsewhere");

        Assert.Equal("value", Assert.Single(operation.Parameters).Name);
    }

    [Fact]
    public async Task TransformAsync_NoPaths_IsLeftAlone()
    {
        var document = new OpenApiDocument { Paths = null! };

        await Run(document, nameof(Handlers.ByGuid), "api/v1/widgets");

        Assert.Null(document.Paths);
    }

    private static async Task<OpenApiOperation> Transform(string handler, params OpenApiParameter[] parameters)
    {
        var operation = new OpenApiOperation { Parameters = [.. parameters] };
        await Run(Document(operation), handler, "api/v1/widgets");
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

    private static Task Run(OpenApiDocument document, string handler, string path)
    {
        var method = typeof(Handlers).GetMethod(handler)!;
        var description = new ApiDescription
        {
            HttpMethod = "GET",
            RelativePath = path,
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

        return new FilteringDocumentTransformer().TransformAsync(document, new OpenApiDocumentTransformerContext
        {
            DocumentName = "v1",
            DescriptionGroups = [new ApiDescriptionGroup("v1", [description])],
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// One handler per filter shape, each filter named <c>value</c>.
    /// </summary>
    public static class Handlers
    {
        /// <summary>
        /// An identifier.
        /// </summary>
        public static void ByGuid(Filter<Guid>? value)
        {
        }

        /// <summary>

        /// Identifiers.

        /// </summary>
        public static void ByGuids(Filter<List<Guid>>? value)
        {
        }

        /// <summary>

        /// An instant.

        /// </summary>
        public static void ByDate(Filter<DateTimeOffset>? value)
        {
        }

        /// <summary>

        /// A local instant.

        /// </summary>
        public static void ByLocalDate(Filter<DateTime>? value)
        {
        }

        /// <summary>

        /// A flag.

        /// </summary>
        public static void ByFlag(Filter<bool>? value)
        {
        }

        /// <summary>

        /// A count.

        /// </summary>
        public static void ByCount(Filter<int>? value)
        {
        }

        /// <summary>

        /// A large count.

        /// </summary>
        public static void ByTotal(Filter<long>? value)
        {
        }

        /// <summary>

        /// A name.

        /// </summary>
        public static void ByName(Filter<string>? value)
        {
        }

        /// <summary>

        /// A shade.

        /// </summary>
        public static void ByShade(Filter<Shade>? value)
        {
        }

        /// <summary>

        /// Instants within a range.

        /// </summary>
        public static void WithinDates(Filter<Range<DateTimeOffset>>? value)
        {
        }

        /// <summary>

        /// Counts within a range.

        /// </summary>
        public static void WithinCounts(Filter<Range<int>>? value)
        {
        }

        /// <summary>

        /// Flags within a range.

        /// </summary>
        public static void WithinFlags(Filter<Range<bool>>? value)
        {
        }

        /// <summary>

        /// No filter.

        /// </summary>
        public static void Plain(string? value)
        {
        }
    }
}
