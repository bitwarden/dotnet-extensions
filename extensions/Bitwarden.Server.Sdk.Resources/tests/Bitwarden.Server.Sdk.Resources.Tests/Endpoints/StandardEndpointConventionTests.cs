using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Bitwarden.Server.Sdk.Authorization;
using Bitwarden.Server.Sdk.Resources.Endpoints;
using Bitwarden.Server.Sdk.Resources.Querying;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Resources.Tests.Endpoints;

/// <summary>
/// What the standards put on a handler's endpoint as it is built: the marker, its class's
/// declarations, the operation identifier and documented response, the settled authorization, and
/// the refusal of a handler taking the person without requiring one.
/// </summary>
public sealed class StandardEndpointConventionTests
{
    [Fact]
    public void Apply_NoHandler_Untouched()
    {
        var endpoint = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("health"), 0)
        {
            ApplicationServices = Services(),
        };

        StandardEndpointConvention.Apply(endpoint);

        Assert.Empty(endpoint.Metadata);
    }

    [Fact]
    public void Apply_Handler_MarkedAsStandard()
    {
        var endpoint = Endpoint(nameof(Handlers.CreateWidget));

        StandardEndpointConvention.Apply(endpoint);

        Assert.Contains(StandardEndpointMetadata.Instance, endpoint.Metadata);
    }

    [Fact]
    public void Apply_OnlyThePlatformsFilter_CountsNoFilters()
    {
        var endpoint = Endpoint(nameof(Handlers.CreateWidget));
        endpoint.FilterFactories.Add(StandardEndpointFilter.Create);

        StandardEndpointConvention.Apply(endpoint);

        Assert.Equal(0, Assert.Single(endpoint.Metadata.OfType<EndpointFilters>()).Count);
    }

    [Fact]
    public void Apply_FiltersBesideThePlatforms_CountsThem()
    {
        var endpoint = Endpoint(nameof(Handlers.CreateWidget));
        endpoint.FilterFactories.Add(StandardEndpointFilter.Create);
        endpoint.FilterFactories.Add((_, next) => next);
        endpoint.FilterFactories.Add((_, next) => next);

        StandardEndpointConvention.Apply(endpoint);

        Assert.Equal(2, Assert.Single(endpoint.Metadata.OfType<EndpointFilters>()).Count);
    }

    // Runtime(...) is a Delegate the request delegate generator can't resolve (RDG002), so the framework's runtime
    // factory builds that endpoint: each test below runs on both of the framework's code paths.
#pragma warning disable RDG002
    // The framework puts its validation filter on every endpoint of a host that calls AddValidation, first and whatever
    // the handler takes; where it has nothing of the service's own to check, it is the platform's, as the standard
    // filter is. Built by a host, as the framework builds it, with the request delegate generator and without.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Apply_HostCallingAddValidation_CountsNoFilters(bool generated)
    {
        var endpoint = BuiltWithValidation(generated
            ? group => group.MapGet("{id}", Handlers.ReadWidget)
            : group => group.MapGet("{id}", Runtime(Handlers.ReadWidget)));

        Assert.Equal(0, Assert.Single(endpoint.Metadata.OfType<EndpointFilters>()).Count);
    }

    // A read as a cacheable one is written: the identifier, the fieldset, a cancellation token and a dependency.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Apply_HostCallingAddValidationForAStandardRead_CountsNoFilters(bool generated)
    {
        var endpoint = BuiltWithValidation(generated
            ? group => group.MapGet("{id}", Handlers.ReadGadget)
            : group => group.MapGet("{id}", Runtime(Handlers.ReadGadget)));

        Assert.Equal(0, Assert.Single(endpoint.Metadata.OfType<EndpointFilters>()).Count);
    }

    // A rule on a parameter is the service's own, and the validation filter refuses a request breaking it before the
    // handler runs.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Apply_HostCallingAddValidationForARuleOnAParameter_CountsTheValidationFilter(bool generated)
    {
        var endpoint = BuiltWithValidation(generated
            ? group => group.MapGet("{id}", Handlers.ReadShortWidget)
            : group => group.MapGet("{id}", Runtime(Handlers.ReadShortWidget)));

        Assert.Equal(1, Assert.Single(endpoint.Metadata.OfType<EndpointFilters>()).Count);
    }

    // A body's type is the service's own, and may carry rules the validation filter checks.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Apply_HostCallingAddValidationForABody_CountsTheValidationFilter(bool generated)
    {
        var endpoint = BuiltWithValidation(generated
            ? group => group.MapPost("", Handlers.CreateWidget)
            : group => group.MapPost("", Runtime(Handlers.CreateWidget)));

        Assert.Equal(1, Assert.Single(endpoint.Metadata.OfType<EndpointFilters>()).Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Apply_HostCallingAddValidationAndAFilterBeside_CountsTheFilter(bool generated)
    {
        var endpoint = BuiltWithValidation(generated
            ? group => group.MapGet("{id}", Handlers.ReadWidget).AddEndpointFilter((context, next) => next(context))
            : group => group.MapGet("{id}", Runtime(Handlers.ReadWidget)).AddEndpointFilter((context, next) => next(context)));

        Assert.Equal(1, Assert.Single(endpoint.Metadata.OfType<EndpointFilters>()).Count);
    }
#pragma warning restore RDG002

    [Theory]
    [InlineData("api/v1/widgets", "createWidget")]
    [InlineData("api/v2/widgets", "createWidgetV2")]
    public void Apply_StandardOperation_NamedForTheMethodAndVersion(string route, string expected)
    {
        var endpoint = Endpoint(nameof(Handlers.CreateWidget), route);

        StandardEndpointConvention.Apply(endpoint);

        Assert.Equal(expected, Assert.Single(endpoint.Metadata.OfType<IEndpointNameMetadata>()).EndpointName);
    }

    [Fact]
    public void Apply_NamedByHand_TheAuthorsName()
    {
        var endpoint = Endpoint(nameof(Handlers.CreateWidget));
        endpoint.Metadata.Add(new EndpointNameMetadata("makeWidget"));

        StandardEndpointConvention.Apply(endpoint);

        Assert.Equal("makeWidget", Assert.Single(endpoint.Metadata.OfType<IEndpointNameMetadata>()).EndpointName);
    }

    [Fact]
    public void Apply_StandardOperation_DocumentsItsResponseInPlaceOfTheInferredOne()
    {
        var endpoint = Endpoint(nameof(Handlers.CreateWidget));
        endpoint.Metadata.Add(new ProducesResponseTypeMetadata(200, typeof(ResourceDocument<Gadget>), ["application/json"]));

        StandardEndpointConvention.Apply(endpoint);

        var actual = Assert.Single(endpoint.Metadata.OfType<IProducesResponseTypeMetadata>());
        Assert.Equal(201, actual.StatusCode);
        Assert.Equal(typeof(ResourceDocument<Gadget>), actual.Type);
        Assert.Equal(["application/json"], actual.ContentTypes);
    }

    [Fact]
    public void Apply_StandardOperationReturningNothing_DocumentsNoBody()
    {
        var endpoint = Endpoint(nameof(Handlers.DeleteWidget));

        StandardEndpointConvention.Apply(endpoint);

        var actual = Assert.Single(endpoint.Metadata.OfType<IProducesResponseTypeMetadata>());
        Assert.Equal(204, actual.StatusCode);
        Assert.Equal(typeof(void), actual.Type);
    }

    [Fact]
    public void Apply_ResponseDeclaredByHand_TheAuthorsResponse()
    {
        var endpoint = Endpoint(nameof(Handlers.ReadWidget));

        StandardEndpointConvention.Apply(endpoint);

        Assert.Empty(endpoint.Metadata.OfType<ProducesResponseTypeMetadata>());
    }

    // As an MVC controller's [Produces("application/json")] makes every answer JSON.
    [Fact]
    public void Apply_PlainHandlerAnsweringText_DocumentedAsJson()
    {
        var endpoint = Endpoint(nameof(Handlers.Ping));
        endpoint.Metadata.Add(new ProducesResponseTypeMetadata(200, typeof(string), ["text/plain"]));

        StandardEndpointConvention.Apply(endpoint);

        var actual = Assert.Single(endpoint.Metadata.OfType<ProducesResponseTypeMetadata>());
        Assert.Equal(200, actual.StatusCode);
        Assert.Equal(typeof(string), actual.Type);
        Assert.Equal(["application/json"], actual.ContentTypes);
        Assert.Empty(endpoint.Metadata.OfType<IEndpointNameMetadata>());
    }

    // Before the handler's own, so the handler's own declaration wins; the compiler's own markers stay off.
    [Fact]
    public void Apply_ClassDeclarations_FirstInTheMetadata()
    {
        var endpoint = Endpoint(nameof(Handlers.CreateWidget));

        StandardEndpointConvention.Apply(endpoint);

        Assert.IsType<TagsAttribute>(endpoint.Metadata[0]);
        Assert.DoesNotContain(endpoint.Metadata, each => each.GetType().Namespace == "System.Runtime.CompilerServices");
    }

    [Theory]
    [InlineData("GET", "widgets.read")]
    [InlineData("POST", "widgets.write")]
    public void Apply_PlainHandler_ReadsTakeTheReadScope(string verb, string scope)
    {
        var endpoint = Endpoint(nameof(Handlers.Ping), services: Services(new AuthorizationDefaults
        {
            ReadScope = "widgets.read",
            WriteScope = "widgets.write",
        }));
        endpoint.Metadata.Add(new HttpMethodMetadata([verb]));

        StandardEndpointConvention.Apply(endpoint);

        Assert.Equal(scope, Assert.Single(OperationAuthorization.SettledIn(endpoint.Metadata)).Scope);
    }

    [Fact]
    public void Apply_PlainHandlerMappedForEveryVerb_TakesTheWriteScope()
    {
        var endpoint = Endpoint(nameof(Handlers.Ping), services: Services(new AuthorizationDefaults
        {
            ReadScope = "widgets.read",
            WriteScope = "widgets.write",
        }));

        StandardEndpointConvention.Apply(endpoint);

        Assert.Equal("widgets.write", Assert.Single(OperationAuthorization.SettledIn(endpoint.Metadata)).Scope);
    }

    [Fact]
    public void Apply_PlainHandlerWithAnEmptyVerbList_TakesTheWriteScope()
    {
        var endpoint = Endpoint(nameof(Handlers.Ping), services: Services(new AuthorizationDefaults
        {
            ReadScope = "widgets.read",
            WriteScope = "widgets.write",
        }));
        endpoint.Metadata.Add(new HttpMethodMetadata([]));

        StandardEndpointConvention.Apply(endpoint);

        Assert.Equal("widgets.write", Assert.Single(OperationAuthorization.SettledIn(endpoint.Metadata)).Scope);
    }

    [Fact]
    public void Apply_StandardRead_TakesTheReadScope()
    {
        var endpoint = Endpoint(nameof(Handlers.ListWidgets), services: Services(new AuthorizationDefaults
        {
            ReadScope = "widgets.read",
            WriteScope = "widgets.write",
        }));
        endpoint.Metadata.Add(new HttpMethodMetadata(["POST"]));

        StandardEndpointConvention.Apply(endpoint);

        Assert.Equal("widgets.read", Assert.Single(OperationAuthorization.SettledIn(endpoint.Metadata)).Scope);
    }

    [Fact]
    public void Apply_PersonWithoutRequiringOne_ReportedAsAProblem()
    {
        var problems = new StandardEndpointProblems();

        StandardEndpointConvention.Apply(Endpoint(nameof(Handlers.Mine), services: Services(problems: problems)));

        Assert.Equal(
            ["'Handlers.Mine' takes the Person, so it must require one: declare User = ContextRequirement.Required on it or its class."],
            problems.All);
    }

    // Outside a service, where nothing collects problems, the refusal is thrown where it is found.
    [Fact]
    public void Apply_PersonWithoutRequiringOneAndNothingCollecting_Thrown()
    {
        var services = new ServiceCollection().AddOptions().BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => StandardEndpointConvention.Apply(Endpoint(nameof(Handlers.Mine), services: services)));
    }

    [Fact]
    public void Apply_PersonRequired_NoProblem()
    {
        var problems = new StandardEndpointProblems();

        StandardEndpointConvention.Apply(Endpoint(nameof(Handlers.Ours), services: Services(problems: problems)));

        Assert.Empty(problems.All);
    }

    // A form an API takes is not a page posting back, so the framework's antiforgery demand is waived.
    [Fact]
    public void Apply_FormModel_NoAntiforgeryToken()
    {
        var endpoint = Endpoint(nameof(Handlers.Upload));

        StandardEndpointConvention.Apply(endpoint);

        Assert.False(Assert.Single(endpoint.Metadata.OfType<IAntiforgeryMetadata>()).RequiresValidation);
    }

    [Fact]
    public void NameOf_Method_ClassAndMethod()
    {
        Assert.Equal("Handlers.Ping", StandardEndpointConvention.NameOf(Method(nameof(Handlers.Ping))));
    }

    [Theory]
    [InlineData(typeof(IResult))]
    [InlineData(typeof(Task<IResult>))]
    [InlineData(typeof(Task))]
    public void ResponseType_NoTypeADocumentCanUse_Nothing(Type returnType)
    {
        Assert.Null(StandardEndpointConvention.ResponseType(returnType));
    }

    [Fact]
    public void ResponseType_Document_TheDocument()
    {
        Assert.Equal(typeof(ResourceDocument<Gadget>), StandardEndpointConvention.ResponseType(typeof(Task<ResourceDocument<Gadget>>)));
    }

    private static MethodInfo Method(string name) => typeof(Handlers).GetMethod(name)!;

    /// <summary>
    /// The handler as a <see cref="Delegate"/> the request delegate generator can't resolve, so the framework's runtime
    /// factory builds its endpoint.
    /// </summary>
    private static Delegate Runtime(Delegate handler) => handler;

    /// <summary>
    /// The one endpoint <paramref name="map"/> maps under the standards, as a host calling <c>AddValidation</c> builds it.
    /// </summary>
    private static Endpoint BuiltWithValidation(Action<RouteGroupBuilder> map)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddValidation();
        builder.Services.AddSingleton(TimeProvider.System);
        var app = builder.Build();
        map(app.MapGroup("api/v1/widgets").WithStandards());

        return Assert.Single(((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints));
    }

    private static RouteEndpointBuilder Endpoint(string handler, string route = "api/v1/widgets", IServiceProvider? services = null)
    {
        var endpoint = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse(route), 0)
        {
            ApplicationServices = services ?? Services(),
        };
        endpoint.Metadata.Add(Method(handler));

        return endpoint;
    }

    private static ServiceProvider Services(AuthorizationDefaults? defaults = null, StandardEndpointProblems? problems = null)
    {
        var services = new ServiceCollection().AddOptions();
        services.AddSingleton(problems ?? new StandardEndpointProblems());

        if (defaults is not null)
        {
            services.AddSingleton(defaults);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The resource the handlers answer with.
    /// </summary>
    public sealed record Gadget : Resource;

    /// <summary>
    /// A form the upload handler takes.
    /// </summary>
    public sealed class GadgetForm
    {
        /// <summary>
        /// A label.
        /// </summary>
        public string? Label { get; set; }
    }

    /// <summary>
    /// The handlers the convention is applied to.
    /// </summary>
    [Tags("Gadgets")]
    public static class Handlers
    {
        /// <summary>
        /// A standard create.
        /// </summary>
        [StandardCreate]
        public static Task<ResourceDocument<Gadget>> CreateWidget(ResourceDocument<Gadget> request) => Task.FromResult(request);

        /// <summary>
        /// A standard delete answering nothing.
        /// </summary>
        [StandardDelete]
        public static Task DeleteWidget(string id) => Task.CompletedTask;

        /// <summary>
        /// A standard read taking what a cacheable read takes.
        /// </summary>
        [StandardReadOne]
        public static Task<ResourceDocument<Gadget>> ReadGadget(
            string id, Fields<Gadget> fields, TimeProvider clock, CancellationToken cancellationToken) =>
            Task.FromResult(new ResourceDocument<Gadget> { Data = new Gadget { Id = id } });

        /// <summary>
        /// A standard read with a rule on its identifier.
        /// </summary>
        [StandardReadOne]
        public static string ReadShortWidget([StringLength(3)] string id) => id;

        /// <summary>
        /// A standard read declaring its own response.
        /// </summary>
        [StandardReadOne]
        [ProducesResponseType<string>(200)]
        public static string ReadWidget(string id) => id;

        /// <summary>
        /// A standard read mapped with POST.
        /// </summary>
        [StandardSearch("api/v1/widgets")]
        public static string ListWidgets() => string.Empty;

        /// <summary>
        /// A plain handler.
        /// </summary>
        public static string Ping() => "pong";

        /// <summary>
        /// A handler taking the person, without requiring one.
        /// </summary>
        [StandardReadMany]
        public static string Mine(Person person) => person.Id.ToString();

        /// <summary>
        /// A handler taking the person, requiring one.
        /// </summary>
        [StandardReadMany]
        [Authorization(User = ContextRequirement.Required)]
        public static string Ours(Person person) => person.Id.ToString();

        /// <summary>
        /// A handler taking a form.
        /// </summary>
        [StandardCreate]
        public static string Upload([FromForm] GadgetForm form) => form.Label ?? string.Empty;
    }
}
