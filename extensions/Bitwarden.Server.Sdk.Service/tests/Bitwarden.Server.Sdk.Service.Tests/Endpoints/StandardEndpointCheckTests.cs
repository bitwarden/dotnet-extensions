using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A service does not start with an endpoint that answers requests but is missing from its
/// OpenAPI document, or whose <c>Map</c> line disagrees with its attribute.
/// </summary>
public sealed class StandardEndpointCheckTests
{
    [Fact]
    public void Build_EndpointInTheDocument_Starts()
    {
        Assert.Null(Record.Exception(() => new Published(hidden: false).Build()));
    }

    [Fact]
    public void Build_EndpointHiddenOnPurpose_RefusesNamingIt()
    {
        var actual = Assert.Throws<InvalidOperationException>(() => new Published(hidden: true).Build());

        Assert.Contains(
            "'PublishedEndpoints.GetWidget' answers requests but is not in the OpenAPI document",
            actual.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_MappedWithAnotherVerb_RefusesNamingIt()
    {
        var actual = Assert.Throws<InvalidOperationException>(() => new Published(hidden: false, verb: HttpMethods.Post).Build());

        Assert.Contains(
            "'PublishedEndpoints.GetWidget' is [StandardReadOne], which is GET, but is mapped with POST.",
            actual.Message,
            StringComparison.Ordinal);
    }

    // Only a standard search or bulk operation has its expression checked against the resource's
    // filterable fields; anywhere else the expression would reach the entity unchecked.
    [Theory]
    [InlineData(nameof(FilteredEndpoints.FindWidgets), "Search")]
    [InlineData(nameof(FilteredEndpoints.TidyWidgets), "BulkDelete")]
    [InlineData(nameof(FilteredEndpoints.MatchWidgets), "Query")]
    public void Build_FilterOutsideAStandardSearchOrBulkOperation_RefusesNamingIt(string handler, string type)
    {
        var actual = Assert.Throws<InvalidOperationException>(() => new Filtered(handler).Build());

        Assert.Contains(
            $"'FilteredEndpoints.{handler}' takes 'request' as a {type}",
            actual.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_FilterOnAStandardSearch_IsNotRefusedForIt()
    {
        var actual = Record.Exception(() => new Filtered(nameof(FilteredEndpoints.SearchWidgets)).Build());

        Assert.DoesNotContain("takes 'request' as a", actual?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    private sealed class Filtered(string handler)
        : Service("filtered-probe", "Filtered Probe", [])
    {
        protected override WebApplicationBuilder CreateBuilder()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            return builder;
        }

        protected override void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
            var widgets = endpoints.MapGroup("api/v1/widgets").WithStandards();

            _ = handler switch
            {
                nameof(FilteredEndpoints.FindWidgets) => widgets.MapPost("find", FilteredEndpoints.FindWidgets),
                nameof(FilteredEndpoints.TidyWidgets) => widgets.MapPost("tidy", FilteredEndpoints.TidyWidgets),
                nameof(FilteredEndpoints.MatchWidgets) => widgets.MapPost("match", FilteredEndpoints.MatchWidgets),
                _ => widgets.MapPost(":search", FilteredEndpoints.SearchWidgets),
            };
        }
    }

    private sealed class Published(bool hidden, string verb = "GET")
        : Service("published-probe", "Published Probe", [])
    {
        protected override WebApplicationBuilder CreateBuilder()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            return builder;
        }

        protected override void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
            var widget = endpoints.MapGroup("api/v1/widgets").WithStandards()
                .MapMethods("{id}", [verb], PublishedEndpoints.GetWidget);

            if (hidden)
            {
                widget.ExcludeFromDescription();
            }
        }
    }
}

/// <summary>
/// What <see cref="StandardEndpointCheckTests"/> publishes.
/// </summary>
public sealed record Widget : Resource;

/// <summary>
/// One standard read.
/// </summary>
public static class PublishedEndpoints
{
    /// <summary>
    /// Read a widget
    /// </summary>
    /// <param name="id">The widget.</param>
    /// <returns>The widget</returns>
    [StandardReadOne]
    public static ResourceDocument<Widget> GetWidget(string id) =>
        StandardResponses.ReadOneResponse(new Widget { Id = id });
}

/// <summary>
/// Handlers taking an expression, with and without the attribute that checks it.
/// </summary>
public static class FilteredEndpoints
{
    /// <summary>
    /// Find widgets
    /// </summary>
    /// <param name="request">The search.</param>
    /// <returns>Nothing</returns>
    [StandardAction]
    public static string FindWidgets(Search<Widget> request) => string.Empty;

    /// <summary>
    /// Tidy widgets
    /// </summary>
    /// <param name="request">Which to remove.</param>
    /// <returns>Nothing</returns>
    [StandardAction]
    public static string TidyWidgets(BulkDelete<Widget> request) => string.Empty;

    /// <summary>
    /// Match widgets
    /// </summary>
    /// <param name="request">The expression.</param>
    /// <returns>Nothing</returns>
    [StandardSearch("api/v1/widgets")]
    public static string MatchWidgets(Query request) => string.Empty;

    /// <summary>
    /// Search widgets
    /// </summary>
    /// <param name="request">The search.</param>
    /// <returns>The widgets</returns>
    [StandardSearch("api/v1/widgets")]
    public static ResourceListDocument<Widget> SearchWidgets(Search<Widget> request) =>
        new() { Data = [] };
}
