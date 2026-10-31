using System.ComponentModel;
using System.IO.Pipelines;
using System.Reflection;
using System.Security.Claims;
using Bitwarden.Server.Sdk.Service.Endpoints;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A handler's parameters are classified once, by where each comes from and whether a request may
/// leave it out, and a parameter the standards cannot bind and validate stops the handler being served.
/// </summary>
public sealed class RequestParametersTests
{
    private static readonly IReadOnlyList<RequestParameter> Classified = Classify(nameof(Handlers.Everything), out _);

    [Theory]
    [InlineData("cancellationToken")]
    [InlineData("http")]
    [InlineData("request")]
    [InlineData("response")]
    [InlineData("user")]
    [InlineData("stream")]
    [InlineData("reader")]
    [InlineData("fromServices")]
    [InlineData("keyed")]
    [InlineData("registered")]
    public void For_FrameworkOrContainer_Ignored(string name)
    {
        Assert.Equal(ParameterKind.Ignored, Named(name).Kind);
    }

    [Theory]
    [InlineData("asParameters", true)]
    [InlineData("optionalAsParameters", false)]
    [InlineData("form", true)]
    public void For_BoundMemberByMember_Model(string name, bool required)
    {
        var actual = Named(name);

        Assert.Equal(ParameterKind.Model, actual.Kind);
        Assert.Equal(required, actual.Required);
    }

    [Theory]
    [InlineData("document", true)]
    [InlineData("optionalDocument", false)]
    public void For_ResourceDocument_Document(string name, bool required)
    {
        var actual = Named(name);

        Assert.Equal(ParameterKind.Document, actual.Kind);
        Assert.Equal(required, actual.Required);
    }

    [Theory]
    [InlineData("person", true)]
    [InlineData("fields", false)]
    [InlineData("sort", false)]
    public void For_BindsItself_Bound(string name, bool required)
    {
        var actual = Named(name);

        Assert.Equal(ParameterKind.Bound, actual.Kind);
        Assert.Equal(required, actual.Required);
    }

    [Theory]
    [InlineData("count", "count", "RouteOrQuery", true)]
    [InlineData("limit", "limit", "RouteOrQuery", false)]
    [InlineData("skip", "skip", "RouteOrQuery", false)]
    [InlineData("name", "name", "RouteOrQuery", true)]
    [InlineData("nickname", "nickname", "RouteOrQuery", false)]
    [InlineData("ids", "ids", "RouteOrQuery", false)]
    [InlineData("trace", "X-Trace", "Header", true)]
    [InlineData("search", "q", "Query", true)]
    [InlineData("id", "id", "Route", true)]
    [InlineData("kind", "kind", "RouteOrQuery", true)]
    public void For_SimpleValue_WhereItIsReadAndWhetherRequired(string name, string modelName, string place, bool required)
    {
        var actual = Named(name);

        Assert.Equal(ParameterKind.Simple, actual.Kind);
        Assert.Equal(modelName, actual.ModelName);
        Assert.Equal(Enum.Parse<ValuePlace>(place), actual.Place);
        Assert.Equal(required, actual.Required);
    }

    // The framework binds T? through T's BindAsync, so a nullable page parameter is read from the
    // query, not the body.
    [Fact]
    public void For_NullableQueryType_Bound()
    {
        var actual = Classify(nameof(Handlers.NullableQuery), out _);

        Assert.All(actual, parameter => Assert.Equal(ParameterKind.Bound, parameter.Kind));
    }

    [Theory]
    [InlineData("body", true)]
    [InlineData("optionalBody", false)]
    public void For_AnythingElse_Body(string name, bool required)
    {
        var actual = Named(name);

        Assert.Equal(ParameterKind.Body, actual.Kind);
        Assert.Equal(required, actual.Required);
    }

    [Fact]
    public void For_EveryParameterClassifiable_InOrderWithoutProblems()
    {
        _ = Classify(nameof(Handlers.Everything), out var problems);

        Assert.Empty(problems);
        Assert.Equal(
            Enumerable.Range(0, Method(nameof(Handlers.Everything)).GetParameters().Length),
            Classified.Select(parameter => parameter.Index));
    }

    // A complex value read from the query or a header, or a single form field, is nothing the standards can check.
    [Fact]
    public void For_Unclassifiable_EachReportedAndLeftOut()
    {
        var actual = Classify(nameof(Handlers.Unservable), out var problems);

        Assert.Equal(["fine"], actual.Select(parameter => parameter.Info.Name));
        Assert.Equal(3, problems.Count);
        Assert.Equal(
            "'Handlers.Unservable' takes 'filter' as a Widgetry, which the platform cannot bind and validate. A handler "
            + "takes route, query and header values of simple types, a resource document, a JSON body, the platform's "
            + "query types, and its dependencies.",
            problems[0]);
        Assert.Contains("takes 'header' as a Widgetry", problems[1], StringComparison.Ordinal);
        Assert.Contains("takes 'field' as a String", problems[2], StringComparison.Ordinal);
    }

    [Fact]
    public void For_NoContainerQuery_AServiceIsABody()
    {
        var problems = new List<string>();

        var actual = RequestParameters.For(Method(nameof(Handlers.Everything)), new Empty(), problems);

        Assert.Equal(ParameterKind.Body, actual.Single(parameter => parameter.Info.Name == "registered").Kind);
    }

    [Theory]
    [InlineData(typeof(Sort))]
    [InlineData(typeof(PageNumber))]
    [InlineData(typeof(PageSize))]
    [InlineData(typeof(PageAfter))]
    [InlineData(typeof(PageNumber?))]
    [InlineData(typeof(Filter<int[]>))]
    [InlineData(typeof(Fields<Thing>))]
    [InlineData(typeof(Fields<Thing>?))]
    public void IsQueryType_PlatformQueryType_True(Type type)
    {
        Assert.True(RequestParameters.IsQueryType(type));
    }

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(Person))]
    [InlineData(typeof(List<int>))]
    [InlineData(typeof(ResourceDocument<Thing>))]
    public void IsQueryType_AnythingElse_False(Type type)
    {
        Assert.False(RequestParameters.IsQueryType(type));
    }

    [Theory]
    [InlineData(typeof(Person), true)]
    [InlineData(typeof(Fields<Thing>), true)]
    [InlineData(typeof(PageNumber?), true)]
    [InlineData(typeof(string), false)]
    [InlineData(typeof(Widgetry), false)]
    public void BindsItself_HasTheFrameworksBindAsync(Type type, bool expected)
    {
        Assert.Equal(expected, RequestParameters.BindsItself(type));
    }

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(bool?))]
    [InlineData(typeof(ConsoleColor))]
    [InlineData(typeof(string))]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(DateTimeOffset?))]
    [InlineData(typeof(DateOnly))]
    [InlineData(typeof(TimeOnly))]
    [InlineData(typeof(TimeSpan))]
    public void IsSimple_WhatABinderConvertsFromAString_True(Type type)
    {
        Assert.True(RequestParameters.IsSimple(type));
    }

    [Theory]
    [InlineData(typeof(int[]))]
    [InlineData(typeof(Uri))]
    [InlineData(typeof(Widgetry))]
    public void IsSimple_AnythingElse_False(Type type)
    {
        Assert.False(RequestParameters.IsSimple(type));
    }

    // In the invariant culture, as an MVC controller's binder converts.
    [Theory]
    [InlineData(typeof(string), "anything", true)]
    [InlineData(typeof(int), "42", true)]
    [InlineData(typeof(int?), "42", true)]
    [InlineData(typeof(int), "4x", false)]
    [InlineData(typeof(decimal), "1.5", true)]
    [InlineData(typeof(decimal), "1,5", false)]
    [InlineData(typeof(Guid), "c3000000-0000-4000-8000-00000000000c", true)]
    [InlineData(typeof(Guid), "nope", false)]
    [InlineData(typeof(ConsoleColor), "Red", true)]
    [InlineData(typeof(bool), "maybe", false)]
    public void Converts_Value_AsABinderWould(Type type, string value, bool expected)
    {
        Assert.Equal(expected, RequestParameters.Converts(type, value));
    }

    [Fact]
    public void BindQueryType_SortAbsent_Null()
    {
        Assert.Null(RequestParameters.BindQueryType(Parameter(nameof(Handlers.Queried), "sort"), Request(string.Empty)));
    }

    // The handler's resource declares nothing sortable, so binding refuses as it would.
    [Fact]
    public void BindQueryType_SortOnNothingSortable_ThrowsWhatBindingWould()
    {
        Assert.Throws<InvalidSortException>(
            () => RequestParameters.BindQueryType(Parameter(nameof(Handlers.Queried), "sort"), Request("?sort=name")));
    }

    [Fact]
    public void BindQueryType_PageNumber_TheValueSent()
    {
        var actual = RequestParameters.BindQueryType(Parameter(nameof(Handlers.Queried), "number"), Request("?page[number]=3"));

        Assert.Equal(3, Assert.IsType<PageNumber>(actual).Value);
    }

    [Fact]
    public void BindQueryType_PageSizeAbsent_TheDeclaredDefault()
    {
        var actual = RequestParameters.BindQueryType(Parameter(nameof(Handlers.Queried), "size"), Request(string.Empty));

        Assert.Equal(20, Assert.IsType<PageSize>(actual).Value);
    }

    [Fact]
    public void BindQueryType_PageAfter_TheCursorSent()
    {
        var actual = RequestParameters.BindQueryType(Parameter(nameof(Handlers.Queried), "after"), Request("?page[after]=abc"));

        Assert.Equal("abc", Assert.IsType<PageAfter>(actual).Value);
    }

    [Fact]
    public void BindQueryType_Filter_ByTheParametersName()
    {
        var actual = RequestParameters.BindQueryType(Parameter(nameof(Handlers.Queried), "id"), Request("?filter[id]=1,2"));

        Assert.Equal([1, 2], Assert.IsType<Filter<int[]>>(actual).Value);
    }

    [Fact]
    public void BindQueryType_Fields_TheNamesSent()
    {
        var actual = RequestParameters.BindQueryType(Parameter(nameof(Handlers.Queried), "fields"), Request("?fields[thing]=note"));

        Assert.Equal(["note"], Assert.IsType<Fields<Thing>>(actual).Names!);
    }

    private static RequestParameter Named(string name) => Classified.Single(parameter => parameter.Info.Name == name);

    private static MethodInfo Method(string name) => typeof(Handlers).GetMethod(name)!;

    private static ParameterInfo Parameter(string method, string name) =>
        Method(method).GetParameters().Single(parameter => parameter.Name == name);

    private static IReadOnlyList<RequestParameter> Classify(string method, out List<string> problems)
    {
        problems = [];
        var services = new ServiceCollection().AddSingleton<Registered>().BuildServiceProvider();

        return RequestParameters.For(Method(method), services, problems);
    }

    private static HttpRequest Request(string query) => new DefaultHttpContext { Request = { QueryString = new QueryString(query) } }.Request;

    /// <summary>
    /// A service provider that cannot say what it holds.
    /// </summary>
    private sealed class Empty : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

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
    /// A complex value.
    /// </summary>
    public sealed class Widgetry
    {
        /// <summary>
        /// A label.
        /// </summary>
        public string? Label { get; set; }
    }

    /// <summary>
    /// A registered dependency.
    /// </summary>
    public sealed class Registered;

    /// <summary>
    /// The handlers whose parameters are classified.
    /// </summary>
    public static class Handlers
    {
        /// <summary>
        /// One parameter of every kind.
        /// </summary>
#pragma warning disable IDE0060
        public static void Everything(
            CancellationToken cancellationToken,
            HttpContext http,
            HttpRequest request,
            HttpResponse response,
            ClaimsPrincipal user,
            Stream stream,
            PipeReader reader,
            [FromServices] Widgetry fromServices,
            [FromKeyedServices("k")] Widgetry keyed,
            Registered registered,
            [AsParameters] Widgetry asParameters,
            [AsParameters] Widgetry? optionalAsParameters,
            [FromForm] Widgetry form,
            ResourceDocument<Thing> document,
            ResourceDocument<Thing>? optionalDocument,
            Person person,
            Sort? sort,
            int count,
            int? limit,
            string name,
            string? nickname,
            int[] ids,
            [FromHeader(Name = "X-Trace")] string trace,
            [FromQuery(Name = "q")] string search,
            [FromRoute] Guid id,
            ConsoleColor kind,
            Widgetry body,
            Widgetry? optionalBody,
            int skip = 0,
            Fields<Thing> fields = default)
        {
        }

        /// <summary>
        /// Parameters nothing can check.
        /// </summary>
        public static void Unservable([FromQuery] Widgetry filter, [FromHeader] Widgetry header, [FromForm] string field, int fine)
        {
        }

        /// <summary>
        /// The platform's query types, nullable.
        /// </summary>
        public static void NullableQuery(PageNumber? number, PageSize? size, PageAfter? after)
        {
        }

        /// <summary>
        /// The platform's query types.
        /// </summary>
        public static string Queried(
            Sort? sort,
            PageNumber number,
            [DefaultValue(20)] PageSize size,
            PageAfter after,
            Filter<int[]>? id,
            Fields<Thing> fields) => string.Empty;
#pragma warning restore IDE0060
    }
}
