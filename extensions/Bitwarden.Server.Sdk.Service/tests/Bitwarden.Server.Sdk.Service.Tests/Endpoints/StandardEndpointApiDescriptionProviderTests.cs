using Bitwarden.Server.Sdk.Service.Endpoints;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A standard endpoint is described for the API explorer as an MVC controller action is: its route
/// without constraints, its class as its controller, each parameter with where it is bound from, the
/// platform's query types under their wire names, a form model field by field, and a body's media
/// types as a controller's JSON reader's.
/// </summary>
public sealed class StandardEndpointApiDescriptionProviderTests
{
    [Theory]
    [InlineData(typeof(GroupsEndpoints), "Groups")]
    [InlineData(typeof(Endpoints), "Endpoints")]
    [InlineData(typeof(Handlers), "Handlers")]
    public void ControllerName_Class_EndpointsSuffixDropped(Type declaring, string expected)
    {
        Assert.Equal(expected, StandardEndpointApiDescriptionProvider.ControllerName(declaring));
    }

    [Fact]
    public void Order_First()
    {
        Assert.Equal(0, new StandardEndpointApiDescriptionProvider(Services()).Order);
    }

    [Fact]
    public void OnProvidersExecuting_Nothing()
    {
        var context = Context(Description(nameof(Handlers.Read), "api/v1/things/{id:guid}"));

        new StandardEndpointApiDescriptionProvider(Services()).OnProvidersExecuting(context);

        Assert.Equal("api/v1/things/{id:guid}", context.Results[0].RelativePath);
    }

    [Fact]
    public void OnProvidersExecuted_NotAStandardEndpoint_Untouched()
    {
        var unmarked = Description(nameof(Handlers.Read), "api/v1/things/{id:guid}", standard: false);
        var bare = new ApiDescription { RelativePath = "x/{id:int}", ActionDescriptor = new ActionDescriptor { EndpointMetadata = null! } };

        Execute(unmarked, bare);

        Assert.Equal("api/v1/things/{id:guid}", unmarked.RelativePath);
        Assert.Equal("x/{id:int}", bare.RelativePath);
    }

    [Fact]
    public void OnProvidersExecuted_StandardEndpointWithoutAHandler_Untouched()
    {
        var description = new ApiDescription
        {
            RelativePath = "x/{id:int}",
            ActionDescriptor = new ActionDescriptor { EndpointMetadata = [StandardEndpointMetadata.Instance] },
        };

        Execute(description);

        Assert.Equal("x/{id:int}", description.RelativePath);
    }

    [Fact]
    public void OnProvidersExecuted_Constraints_DroppedFromThePath()
    {
        var description = Description(nameof(Handlers.Read), "api/v1/things/{id:guid}/parts/{part:int:min(1)}");

        Execute(description);

        Assert.Equal("api/v1/things/{id}/parts/{part}", description.RelativePath);
    }

    [Fact]
    public void OnProvidersExecuted_Handler_ItsClassIsTheController()
    {
        var description = Description(nameof(GroupsEndpoints.ListGroups), "api/v1/groups", typeof(GroupsEndpoints));

        Execute(description);

        Assert.Equal("Groups", description.ActionDescriptor.RouteValues["controller"]);
        Assert.Equal("ListGroups", description.ActionDescriptor.RouteValues["action"]);
    }

    [Fact]
    public void OnProvidersExecuted_Parameters_DescribedWithWhereTheyAreBound()
    {
        var description = Description(nameof(Handlers.Read), "api/v1/things/{id}");

        Execute(description);

        var actual = description.ActionDescriptor.Parameters.Cast<HandlerParameterDescriptor>().ToDictionary(each => each.Name);
        Assert.Equal(BindingSource.Path, actual["id"].BindingInfo!.BindingSource);
        Assert.Equal(BindingSource.Header, actual["trace"].BindingInfo!.BindingSource);
        Assert.Equal("X-Trace", actual["trace"].BindingInfo!.BinderModelName);
        Assert.Equal(BindingSource.Query, actual["q"].BindingInfo!.BindingSource);
        Assert.Null(actual["q"].BindingInfo!.BinderModelName);
        Assert.Null(actual["loose"].BindingInfo!.BindingSource);
        Assert.Equal(BindingSource.Custom, actual["number"].BindingInfo!.BindingSource);
        Assert.Equal(BindingSource.Services, actual["cancellationToken"].BindingInfo!.BindingSource);
        Assert.Same(typeof(Handlers).GetMethod(nameof(Handlers.Read))!.GetParameters()[0], actual["id"].ParameterInfo);
    }

    // The framework does not describe them at all; a controller describes them under their wire names.
    [Fact]
    public void OnProvidersExecuted_QueryTypes_DescribedUnderTheirWireNames()
    {
        var description = Description(nameof(Handlers.Page), "api/v1/things");

        Execute(description);

        var actual = description.ParameterDescriptions.ToDictionary(each => each.Name);
        Assert.Equal(["page[number]", "page[size]", "page[after]", "fields", "sort"], actual.Keys);
        Assert.All(actual.Values, each => Assert.Equal(BindingSource.Custom, each.Source));
        Assert.All(actual.Values, each => Assert.False(each.IsRequired));
        Assert.Equal(default(PageNumber), actual["page[number]"].DefaultValue);
        Assert.Equal(default(Fields<Thing>), actual["fields"].DefaultValue);
        Assert.Null(actual["sort"].DefaultValue);
        Assert.Equal(default(PageSize), actual["page[size]"].DefaultValue);
    }

    // Kept, and in the handler's order, with what the framework described that is not a parameter after.
    [Fact]
    public void OnProvidersExecuted_FrameworksDescriptions_InTheHandlersOrder()
    {
        var description = Description(nameof(Handlers.Read), "api/v1/things/{id}");
        description.ParameterDescriptions.Add(Described("extra", BindingSource.Query));
        description.ParameterDescriptions.Add(Described("q", BindingSource.Query));
        description.ParameterDescriptions.Add(Described("id", BindingSource.Path));
        description.ParameterDescriptions.Add(new ApiParameterDescription { Name = "raw", Source = BindingSource.Body, Type = typeof(void) });

        Execute(description);

        Assert.Equal(["id", "q", "page[number]", "extra"], description.ParameterDescriptions.Select(each => each.Name));
        Assert.IsType<HandlerParameterDescriptor>(description.ParameterDescriptions[0].ParameterDescriptor);
    }

    [Fact]
    public void OnProvidersExecuted_BodyRoutingDoesNotRestrict_AControllersJsonMediaTypes()
    {
        var description = Description(nameof(Handlers.Write), "api/v1/things");
        description.ParameterDescriptions.Add(Described("note", BindingSource.Body));

        Execute(description);

        Assert.Equal(["application/json", "text/json", "application/*+json"], description.SupportedRequestFormats.Select(each => each.MediaType));
        Assert.Equal("note", Assert.Single(description.ParameterDescriptions).Name);
    }

    [Fact]
    public void OnProvidersExecuted_BodyWithDeclaredFormats_Kept()
    {
        var description = Description(nameof(Handlers.Write), "api/v1/things");
        description.SupportedRequestFormats.Add(new ApiRequestFormat { MediaType = "application/merge-patch+json" });
        description.ParameterDescriptions.Add(Described("note", BindingSource.Body));

        Execute(description);

        Assert.Equal(["application/merge-patch+json"], description.SupportedRequestFormats.Select(each => each.MediaType));
        Assert.Equal("note", Assert.Single(description.ParameterDescriptions).Name);
    }

    [Fact]
    public void OnProvidersExecuted_Document_NamedForTheParameterAsJson()
    {
        var description = Description(nameof(Handlers.Create), "api/v1/things");
        description.SupportedRequestFormats.Add(new ApiRequestFormat { MediaType = "application/vnd.api+json" });
        description.ParameterDescriptions.Add(Described("ResourceDocument", BindingSource.Body));

        Execute(description);

        var actual = Assert.Single(description.ParameterDescriptions);
        Assert.Equal("request", actual.Name);
        Assert.NotNull(actual.ModelMetadata);
        Assert.Equal(["application/json", "text/json", "application/*+json"], description.SupportedRequestFormats.Select(each => each.MediaType));
    }

    [Fact]
    public void OnProvidersExecuted_FormModel_FieldByFieldUnderTheirFormNames()
    {
        var description = Description(nameof(Handlers.Upload), "api/v1/things");
        description.SupportedRequestFormats.Add(new ApiRequestFormat { MediaType = "multipart/form-data" });
        description.ParameterDescriptions.Add(new ApiParameterDescription { Name = "form", Source = BindingSource.Body, Type = typeof(ThingForm) });

        Execute(description);

        Assert.Equal(["n", "Label"], description.ParameterDescriptions.Select(each => each.Name));
        Assert.All(description.ParameterDescriptions, each => Assert.Equal(BindingSource.Form, each.Source));
        Assert.Equal(["application/x-www-form-urlencoded"], description.SupportedRequestFormats.Select(each => each.MediaType));
    }

    [Fact]
    public void OnProvidersExecuted_FormModelDeclaringWhatItConsumes_ThoseMediaTypes()
    {
        var description = Description(nameof(Handlers.Upload), "api/v1/things", extra: [new ConsumesAttribute("multipart/form-data")]);

        Execute(description);

        Assert.Equal(["multipart/form-data"], description.SupportedRequestFormats.Select(each => each.MediaType));
    }

    private static void Execute(params ApiDescription[] descriptions) =>
        new StandardEndpointApiDescriptionProvider(Services()).OnProvidersExecuted(Context(descriptions));

    private static ApiDescriptionProviderContext Context(params ApiDescription[] descriptions)
    {
        var context = new ApiDescriptionProviderContext([]);

        foreach (var each in descriptions)
        {
            context.Results.Add(each);
        }

        return context;
    }

    private static ApiDescription Description(
        string handler, string path, Type? declaring = null, bool standard = true, object[]? extra = null)
    {
        var method = (declaring ?? typeof(Handlers)).GetMethod(handler)!;
        List<object> metadata = standard ? [StandardEndpointMetadata.Instance, method] : [method];
        metadata.AddRange(extra ?? []);

        return new ApiDescription
        {
            RelativePath = path,
            ActionDescriptor = new ActionDescriptor { EndpointMetadata = metadata },
        };
    }

    private static ApiParameterDescription Described(string name, BindingSource source) => new()
    {
        Name = name,
        Source = source,
        ParameterDescriptor = new ParameterDescriptor { Name = name, ParameterType = typeof(object) },
    };

    private static ServiceProvider Services() => new ServiceCollection().BuildServiceProvider();

    /// <summary>
    /// A resource.
    /// </summary>
    public sealed record Thing : Resource
    {
        /// <summary>
        /// A note.
        /// </summary>
        public string? Note { get; init; }
    }

    /// <summary>
    /// A form.
    /// </summary>
    public sealed class ThingForm
    {
        /// <summary>
        /// Sent as <c>n</c>.
        /// </summary>
        [FromForm(Name = "n")]
        public int Count { get; set; }

        /// <summary>
        /// Sent by its own name.
        /// </summary>
        public string? Label { get; set; }

        /// <summary>
        /// Not bound.
        /// </summary>
        public int Computed => Count;
    }

    /// <summary>
    /// The handlers described.
    /// </summary>
    public static class Handlers
    {
#pragma warning disable IDE0060
        /// <summary>
        /// Simple values and a query type.
        /// </summary>
        public static string Read(
            [FromRoute] Guid id,
            [FromHeader(Name = "X-Trace")] string trace,
            [FromQuery] string q,
            string loose,
            PageNumber number,
            CancellationToken cancellationToken) => string.Empty;

        /// <summary>
        /// The platform's query types, with and without defaults.
        /// </summary>
        public static string Page(
            PageNumber number = default,
            PageSize size = default,
            PageAfter after = default,
            Fields<Thing> fields = default,
            Sort? sort = null) => string.Empty;

        /// <summary>
        /// A plain body.
        /// </summary>
        public static string Write(ThingForm note) => string.Empty;

        /// <summary>
        /// A resource document.
        /// </summary>
        public static string Create(ResourceDocument<Thing> request) => string.Empty;

        /// <summary>
        /// A form model.
        /// </summary>
        public static string Upload([FromForm] ThingForm form) => string.Empty;
#pragma warning restore IDE0060
    }

    /// <summary>
    /// A class named for its resource.
    /// </summary>
    public static class GroupsEndpoints
    {
        /// <summary>
        /// A handler.
        /// </summary>
        public static string ListGroups() => string.Empty;
    }

    /// <summary>
    /// A class named only for the suffix.
    /// </summary>
    public static class Endpoints;
}
