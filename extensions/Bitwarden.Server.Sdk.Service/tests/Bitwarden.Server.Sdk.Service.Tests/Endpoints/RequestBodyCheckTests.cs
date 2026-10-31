using System.Reflection;
using System.Text;
using System.Text.Json;
using Bitwarden.Server.Sdk.Service.Endpoints;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A plain JSON body is read before the framework reads it, where a controller's model binding
/// would run: one that is not JSON is a <c>415</c>, one that cannot be read is a <c>422</c> together
/// with what else is wrong with the request, and one that reads is handed on unchanged. The
/// operation's own authorization runs first.
/// </summary>
public sealed class RequestBodyCheckTests
{
    [Fact]
    public void Apply_NoRequestDelegate_Nothing()
    {
        var endpoint = Endpoint(nameof(Unguarded.Plain), inner: null);

        RequestBodyCheck.Apply(endpoint, Method(nameof(Unguarded.Plain)), Services());

        Assert.Null(endpoint.RequestDelegate);
    }

    [Fact]
    public void Apply_NoBodyAndNoAuthorization_RequestDelegateKept()
    {
        RequestDelegate inner = _ => Task.CompletedTask;
        var endpoint = Endpoint(nameof(Unguarded.Plain), inner);

        RequestBodyCheck.Apply(endpoint, Method(nameof(Unguarded.Plain)), Services());

        Assert.Same(inner, endpoint.RequestDelegate);
    }

    // So a request of no media type meets the operation's authorization before it is refused.
    [Fact]
    public void Apply_Body_InferredMediaTypesLeftToTheCheck()
    {
        var endpoint = Endpoint(nameof(Handlers.Takes), _ => Task.CompletedTask);
        endpoint.Metadata.Add(new AcceptsMetadata(["application/json"], typeof(Note)));

        RequestBodyCheck.Apply(endpoint, Method(nameof(Handlers.Takes)), Services());

        Assert.Empty(endpoint.Metadata.OfType<IAcceptsMetadata>());
    }

    [Fact]
    public void Apply_BodyWithDeclaredMediaTypes_KeptForRouting()
    {
        var endpoint = Endpoint(nameof(Handlers.Takes), _ => Task.CompletedTask);
        var inferred = new AcceptsMetadata(["application/json"], typeof(Note));
        var declared = new ConsumesAttribute("application/json");
        endpoint.Metadata.Add(inferred);
        endpoint.Metadata.Add(declared);

        RequestBodyCheck.Apply(endpoint, Method(nameof(Handlers.Takes)), Services());

        Assert.Equal<object>([inferred, declared], endpoint.Metadata.OfType<IAcceptsMetadata>());
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("application/vnd.api+json")]
    public async Task Body_Json_HandedOnUnchanged(string contentType)
    {
        var (http, ran) = await Send(nameof(Handlers.Takes), """{"text":"t"}""", contentType);

        Assert.True(ran.Value);
        Assert.Equal("""{"text":"t"}""", ran.Body);
        Assert.Equal(contentType, http.Request.ContentType);
    }

    // A controller reads it, and the framework would refuse it, so it is handed on as JSON.
    [Fact]
    public async Task Body_TextJson_HandedOnAsJson()
    {
        var (http, ran) = await Send(nameof(Handlers.Takes), """{"text":"t"}""", "text/json");

        Assert.True(ran.Value);
        Assert.Equal("application/json", http.Request.ContentType);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    [InlineData("not a media type")]
    [InlineData(null)]
    public async Task Body_NotJson_UnsupportedMediaType(string? contentType)
    {
        var (http, ran) = await Send(nameof(Handlers.Takes), """{"text":"t"}""", contentType);

        Assert.False(ran.Value);
        Assert.Equal(415, http.Response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    public async Task Body_NoneWhereRequired_Unprocessable(string body)
    {
        var (http, ran) = await Send(nameof(Handlers.Takes), body, "application/json");

        Assert.False(ran.Value);
        Assert.Equal(422, http.Response.StatusCode);
        Assert.Equal(["A non-empty request body is required."], Details(http));
    }

    // No body and no media type is no body, which an optional body may be.
    [Theory]
    [InlineData("", null)]
    [InlineData("", "application/json")]
    [InlineData("null", "application/json")]
    public async Task Body_NoneWhereOptional_HandedOn(string body, string? contentType)
    {
        var (_, ran) = await Send(nameof(Handlers.Optional), body, contentType);

        Assert.True(ran.Value);
    }

    [Fact]
    public async Task Body_Unreadable_UnprocessableNamingWhere()
    {
        var (http, ran) = await Send(nameof(Handlers.Takes), """{"text":5}""", "application/json");

        Assert.False(ran.Value);
        Assert.Equal(422, http.Response.StatusCode);
        var error = Assert.Single(Errors(http));
        Assert.Equal("/text", error.GetProperty("source").GetProperty("pointer").GetString());
    }

    // Refused together, as a controller's model state holds them together.
    [Fact]
    public async Task Body_UnreadableWithABadQueryValue_BothRefused()
    {
        var (http, _) = await Send(nameof(Handlers.WithQuery), "{nope", "application/json", "?count=many");

        Assert.Equal(422, http.Response.StatusCode);
        var details = Details(http);
        Assert.Equal(2, details.Count);
        Assert.Contains("The value 'many' is not valid.", details);
    }

    [Fact]
    public async Task Body_UnreadableWithARequiredFilterAbsent_BothRefused()
    {
        var (http, _) = await Send(nameof(Handlers.WithFilter), "{nope", "application/json");

        Assert.Equal(422, http.Response.StatusCode);
        Assert.Contains("The id field is required.", Details(http));
    }

    // A query value the platform's own types cannot read throws as binding would, and that answer wins.
    [Fact]
    public async Task Body_UnreadableWithAnUnreadableSort_TheSortsRefusal()
    {
        await Assert.ThrowsAsync<InvalidSortException>(
            () => Send(nameof(Handlers.WithSort), "{nope", "application/json", "?sort=nothing"));
    }

    [Fact]
    public async Task AcceptsInvalid_Unreadable_HandedOnAsNothingWithTheBytesKept()
    {
        var (http, ran) = await Send(nameof(Handlers.Lenient), "{nope", "application/json");

        Assert.True(ran.Value);
        Assert.Equal("null", ran.Body);
        Assert.Equal("{nope", Encoding.UTF8.GetString(http.Features.Get<UnreadableRequestBody>()!.Body.Span));
    }

    [Fact]
    public async Task AcceptsInvalid_NoBody_HandedOnAsNothingWithNothingKept()
    {
        var (http, ran) = await Send(nameof(Handlers.Lenient), string.Empty, null);

        Assert.True(ran.Value);
        Assert.Null(http.Features.Get<UnreadableRequestBody>());
    }

    // A controller without [ApiController] cannot read it either.
    [Fact]
    public async Task AcceptsInvalid_NotJson_StillUnsupportedMediaType()
    {
        var (http, ran) = await Send(nameof(Handlers.Lenient), "text", "text/plain");

        Assert.False(ran.Value);
        Assert.Equal(415, http.Response.StatusCode);
    }

    [Fact]
    public async Task Authorization_Declared_RunsBeforeTheBodyIsRead()
    {
        var (_, ran) = await Send(nameof(Handlers.Guarded), "not json", "text/plain");

        Assert.False(ran.Value);
        Assert.Equal(["class", "method"], ran.Authorized);
    }

    [Fact]
    public async Task Authorization_Refusing_TheHandlerNeverRuns()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Send(nameof(Handlers.Refused), string.Empty, null));
    }

    private static async Task<(DefaultHttpContext Http, Ran Ran)> Send(string handler, string body, string? contentType, string query = "")
    {
        var ran = new Ran();
        var endpoint = Endpoint(handler, async http =>
        {
            ran.Value = true;
            ran.Body = await new StreamReader(http.Request.Body).ReadToEndAsync();
        });
        var services = Services();
        RequestBodyCheck.Apply(endpoint, Method(handler), services);

        var http = new DefaultHttpContext { RequestServices = services };
        http.Items[typeof(Ran)] = ran;
        http.Request.Method = HttpMethods.Post;
        http.Request.QueryString = new QueryString(query);
        http.Request.ContentType = contentType;
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        http.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(http);

        return (http, ran);
    }

    private static List<JsonElement> Errors(HttpContext http)
    {
        http.Response.Body.Position = 0;
        using var document = JsonDocument.Parse(http.Response.Body);

        return [.. document.RootElement.GetProperty("errors").EnumerateArray().Select(error => error.Clone())];
    }

    private static List<string?> Details(HttpContext http) =>
        [.. Errors(http).Select(error => error.GetProperty("detail").GetString())];

    private static MethodInfo Method(string name) =>
        typeof(Handlers).GetMethod(name) ?? typeof(Unguarded).GetMethod(name)!;

    private static RouteEndpointBuilder Endpoint(string handler, RequestDelegate? inner)
    {
        var endpoint = new RouteEndpointBuilder(inner, RoutePatternFactory.Parse("api/v1/notes"), 0);
        endpoint.Metadata.Add(Method(handler));

        return endpoint;
    }

    private static ServiceProvider Services() => new ServiceCollection().AddOptions().AddLogging().BuildServiceProvider();

    /// <summary>
    /// What reached the handler.
    /// </summary>
    public sealed class Ran
    {
        /// <summary>
        /// Whether it ran.
        /// </summary>
        public bool Value { get; set; }

        /// <summary>
        /// The body it would read.
        /// </summary>
        public string? Body { get; set; }

        /// <summary>
        /// The authorizations that ran, in order.
        /// </summary>
        public List<string> Authorized { get; } = [];
    }

    /// <summary>
    /// A plain JSON body.
    /// </summary>
    public sealed class Note
    {
        /// <summary>
        /// Some text.
        /// </summary>
        public string? Text { get; set; }
    }

    /// <summary>
    /// Records that it ran, under its name.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class RecordsAttribute(string name, bool refuse = false) : Attribute, IAuthorizesOperation
    {
        /// <inheritdoc />
        public ValueTask AuthorizeAsync(HttpContext context)
        {
            if (refuse)
            {
                throw new UnauthorizedAccessException();
            }

            ((Ran)context.Items[typeof(Ran)]!).Authorized.Add(name);

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Handlers whose bodies are checked.
    /// </summary>
    [Records("class")]
    public static class Handlers
    {
#pragma warning disable IDE0060
        /// <summary>
        /// A required body.
        /// </summary>
        public static void Takes(Note note)
        {
        }

        /// <summary>
        /// An optional body.
        /// </summary>
        public static void Optional(Note? note)
        {
        }

        /// <summary>
        /// A body and a query value.
        /// </summary>
        public static void WithQuery(Note note, [FromQuery] int count)
        {
        }

        /// <summary>
        /// A body and a required filter.
        /// </summary>
        public static void WithFilter(Note note, Filter<int[]> id)
        {
        }

        /// <summary>
        /// A body and a sort.
        /// </summary>
        public static void WithSort(Note note, Sort? sort)
        {
        }

        /// <summary>
        /// A body, letting invalid requests through.
        /// </summary>
        [AcceptsInvalidRequests]
        public static void Lenient(Note? note)
        {
        }

        /// <summary>
        /// A body behind an authorization of its own.
        /// </summary>
        [Records("method")]
        public static void Guarded(Note note)
        {
        }

        /// <summary>
        /// No body, behind an authorization that refuses.
        /// </summary>
        [Records("method", refuse: true)]
        public static void Refused()
        {
        }
#pragma warning restore IDE0060
    }

    /// <summary>
    /// A handler with no body and no authorization of its own.
    /// </summary>
    public static class Unguarded
    {
        /// <summary>
        /// No body.
        /// </summary>
        public static void Plain(int count) => _ = count;
    }
}
