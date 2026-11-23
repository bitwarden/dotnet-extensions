using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Bitwarden.Server.Sdk.Environment;
using Bitwarden.Server.Sdk.Errors;
using Bitwarden.Server.Sdk.Resources.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Resources.Tests.Endpoints;

/// <summary>
/// The filter refuses an operation the install does not have, keeps a status the handler chose,
/// and gives a create, when the handler names none, a <c>Location</c> that is the request's own
/// address and the new resource's identifier. It holds the handler's parameters to the validation
/// attributes they carry, refusing a value that breaks one as a body's member is refused.
/// </summary>
public sealed class StandardEndpointFilterTests
{
    [Fact]
    public void Succeeded_Created_LocationIsTheCollectionAndId()
    {
        var http = Created(pathBase: string.Empty);

        Assert.Equal("/api/v1/widgets/abc", http.Response.Headers.Location.ToString());
    }

    // A service mapped below a path base is reached through it, so the address must name it.
    [Fact]
    public void Succeeded_CreatedBelowAPathBase_LocationIncludesIt()
    {
        var http = Created(pathBase: "/widget-service");

        Assert.Equal("/widget-service/api/v1/widgets/abc", http.Response.Headers.Location.ToString());
    }

    [Fact]
    public void Succeeded_HandlerChoseItsStatus_Kept()
    {
        var http = new DefaultHttpContext();
        http.Response.StatusCode = StatusCodes.Status202Accepted;

        StandardEndpointFilter.Succeeded(
            http, new ResourceDocument<Widget> { Data = new Widget { Id = "abc" } }, StatusCodes.Status201Created);

        Assert.Equal(StatusCodes.Status202Accepted, http.Response.StatusCode);
        Assert.False(http.Response.Headers.ContainsKey("Location"));
    }

    [Theory]
    [InlineData(nameof(Handlers.SelfHostedOnly))]
    [InlineData(nameof(SelfHostedHandlers.SelfHostedByItsClass))]
    public async Task Create_SelfHostedOnlyOffSelfHosted_Refused(string handler)
    {
        var refused = await Assert.ThrowsAsync<InvalidRequestException>(() => Invoke(handler, selfHosted: false));

        Assert.Equal("Only allowed when self hosted.", refused.Message);
    }

    [Theory]
    [InlineData(nameof(Handlers.NotSelfHostedOnly))]
    [InlineData(nameof(CloudHandlers.NotSelfHostedByItsClass))]
    public async Task Create_NotSelfHostedOnlyOnSelfHosted_Refused(string handler)
    {
        var refused = await Assert.ThrowsAsync<InvalidRequestException>(() => Invoke(handler, selfHosted: true));

        Assert.Equal("Only allowed when not self hosted.", refused.Message);
    }

    [Theory]
    [InlineData(nameof(Handlers.SelfHostedOnly), true)]
    [InlineData(nameof(Handlers.NotSelfHostedOnly), false)]
    [InlineData(nameof(Handlers.Anywhere), true)]
    [InlineData(nameof(Handlers.Anywhere), false)]
    public async Task Create_OperationTheInstallHas_HandlerRuns(string handler, bool selfHosted)
    {
        Assert.True(await Invoke(handler, selfHosted));
    }

    // Each case: the route, query and headers sent, then the key the refusal names, its words and whether it names a
    // query parameter (otherwise it points, as a route or header value's binding failure does). A controller calls the
    // parameter by its own name and keys it by the name it is sent under.
    public static TheoryData<bool, string, string, string, string, string, bool> Broken() => Cases(
    [
        ("abcd/5/ab", "owner=me", "X-Owner:me", "id", "The field id must be a string with a maximum length of 3.", false),
        ("abc/500/ab", "owner=me", "X-Owner:me", "number", "The field number must be between 1 and 100.", false),
        ("abc/5/AB", "owner=me", "X-Owner:me", "slug", "The field slug must match the regular expression '^[a-z]+$'.", false),
        ("abc/5/ab", "owner=me&count=0", "X-Owner:me", "count", "The field count must be between 1 and 100.", true),
        ("abc/5/ab", "owner=me&label=far-too-long", "X-Owner:me", "label", "The field label must be a string with a maximum length of 5.", true),
        ("abc/5/ab", "owner=me&tag=AB", "X-Owner:me", "tag", "The field tagged must match the regular expression '^[a-z]+$'.", true),
        ("abc/5/ab", "", "X-Owner:me", "owner", "The owner field is required.", true),
        ("abc/5/ab", "owner=", "X-Owner:me", "owner", "The owner field is required.", true),
        ("abc/5/ab", "owner=me", "X-Owner:me|X-Count:0", "X-Count", "The field shelfCount must be between 1 and 9.", false),
        ("abc/5/ab", "owner=me", "X-Owner:me|X-Name:far-too-long", "X-Name", "The field shelfName must be a string with a maximum length of 5.", false),
        ("abc/5/ab", "owner=me", "X-Owner:me|X-Tag:AB", "X-Tag", "The field shelfTag must match the regular expression '^[a-z]+$'.", false),
        ("abc/5/ab", "owner=me", "", "X-Owner", "The shelfOwner field is required.", false),
        ("abc/5/ab", "owner=me&size=0", "X-Owner:me", "size", "The field Page size must be between 1 and 10.", true),
        ("abc/5/ab", "owner=me&tags=a", "X-Owner:me", "tags", "The field tags must be a string or array type with a minimum length of '2'.", true),
    ]);

    [Theory]
    [MemberData(nameof(Broken))]
    public async Task Create_ParameterBreakingItsRule_RefusedAsABodysMemberIs(
        bool generated, string route, string query, string headers, string key, string message, bool queryParameter)
    {
        var (status, errors, ran) = await Send(generated, $"api/v1/rules/{route}", query, headers);

        Assert.False(ran);
        Assert.Equal(422, status);
        var error = Assert.Single(errors);
        Assert.Equal("422", error.Status);
        Assert.Equal(ErrorCodes.InvalidValue, error.Code);
        Assert.Equal(ErrorTitles.InvalidValue, error.Title);
        Assert.Equal(message, error.Detail);
        Assert.Equal(queryParameter ? key : null, error.Parameter);
        Assert.Equal(queryParameter ? null : RequestPointer.For(key), error.Pointer);
    }

    // Values that keep every rule, and rules with nothing sent to hold to them, since a controller validates a value
    // nobody sent only when it must be sent.
    public static TheoryData<bool, string, string, string> Kept() => Cases(
    [
        ("abc/5/ab", "owner=me", "X-Owner:me"),
        ("abc/100/z", "owner=me&count=100&label=short&tag=ab&size=10&tags=a&tags=b", "X-Owner:me|X-Count:9|X-Name:short|X-Tag:ab"),
        ("ab/1/a", "owner=me&label=&tag=", "X-Owner:me"),

        // A blank value is none, to a controller's binder.
        ("ab/1/a", "owner=me&tag=%20", "X-Owner:me|X-Tag: "),
    ]);

    [Theory]
    [MemberData(nameof(Kept))]
    public async Task Create_ParametersKeepingTheirRules_HandlerRuns(bool generated, string route, string query, string headers)
    {
        var (status, _, ran) = await Send(generated, $"api/v1/rules/{route}", query, headers);

        Assert.True(ran);
        Assert.Equal(200, status);
    }

    // Every failure together, in model state's order, as a body's are.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_SeveralParametersBreakingRules_AllRefusedTogether(bool generated)
    {
        var (_, errors, _) = await Send(generated, "api/v1/rules/abcd/0/ab", "owner=me&count=0", "X-Owner:me");

        Assert.Equal(
            ["The field id must be a string with a maximum length of 3.", "The field count must be between 1 and 100.", "The field number must be between 1 and 100."],
            errors.Select(error => error.Detail));
    }

    // A value that does not bind is refused for that alone, as a controller does not validate it.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_ParameterThatDoesNotBind_RefusedOnlyForThat(bool generated)
    {
        var (_, errors, _) = await Send(generated, "api/v1/rules/abc/many/ab", "owner=me", "X-Owner:me");

        Assert.Equal(["The value 'many' is not valid."], errors.Select(error => error.Detail));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_BodyParameterBreakingItsRule_RefusedPointingAtTheBody(bool generated)
    {
        var (status, errors, ran) = await Send(generated, "api/v1/rules", string.Empty, string.Empty, """[{"text":"a"},{"text":"b"},{"text":"c"}]""");

        Assert.False(ran);
        Assert.Equal(422, status);
        var error = Assert.Single(errors);
        Assert.Equal("The field notes must be a string or array type with a maximum length of '2'.", error.Detail);
        Assert.Equal(RequestPointer.For(string.Empty), error.Pointer);
    }

    // The parameter's own rules run once what is inside the body passes, as a controller runs a model's own.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_BodyWhoseMembersFail_ParameterRuleNotRun(bool generated)
    {
        var (_, errors, _) = await Send(generated, "api/v1/rules", string.Empty, string.Empty, """[{"text":"a"},{"text":"b"},{}]""");

        Assert.Equal(["The Text field is required."], errors.Select(error => error.Detail));
    }

    // A body that cannot be read is refused before the framework binds anything, together with the values beside it
    // that break their rules, as a controller's model state holds both.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_UnreadableBodyAndAParameterBreakingItsRule_RefusedTogether(bool generated)
    {
        var (status, errors, ran) = await Send(generated, "api/v1/rules", "limit=9", string.Empty, "[");

        Assert.False(ran);
        Assert.Equal(422, status);
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, error => error.Detail == "The field limit must be between 1 and 5." && error.Parameter == "limit");
    }

    // A rule on a parameter of any other kind is held too, once what the parameter holds passes, and points at the
    // document. An optional body sent as none is held to its rules as none.
    [Theory]
    [InlineData(true, "api/v1/rules/document", """{"data":{"type":"widget","attributes":{}}}""", "The request is refused.")]
    [InlineData(false, "api/v1/rules/document", """{"data":{"type":"widget","attributes":{}}}""", "The request is refused.")]
    [InlineData(true, "api/v1/rules/model", null, "The query is refused.")]
    [InlineData(false, "api/v1/rules/model", null, "The query is refused.")]
    [InlineData(true, "api/v1/rules/bound", null, "The pageNumber is refused.")]
    [InlineData(false, "api/v1/rules/bound", null, "The pageNumber is refused.")]
    [InlineData(true, "api/v1/rules/optional", "null", "The notes field is required.")]
    [InlineData(false, "api/v1/rules/optional", "null", "The notes field is required.")]
    public async Task Create_RuleOnAnotherKindOfParameter_Refused(bool generated, string path, string? body, string message)
    {
        var (status, errors, ran) = await Send(generated, path, string.Empty, string.Empty, body);

        Assert.False(ran);
        Assert.Equal(422, status);
        var error = Assert.Single(errors);
        Assert.Equal(message, error.Detail);
        Assert.Equal(RequestPointer.For(string.Empty), error.Pointer);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_BodyKeepingItsRule_HandlerRuns(bool generated)
    {
        var (status, _, ran) = await Send(generated, "api/v1/rules", string.Empty, string.Empty, """[{"text":"a"}]""");

        Assert.True(ran);
        Assert.Equal(200, status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_AcceptsInvalidRequests_ParameterBreakingItsRuleReachesTheHandler(bool generated)
    {
        var (status, _, ran) = await Send(generated, "api/v1/lenient/abcd", "count=0", string.Empty);

        Assert.True(ran);
        Assert.Equal(200, status);
    }

    // A host calling AddValidation gives every endpoint the framework's validation filter, which would refuse first, in
    // its own words, and whatever the handler accepts. The standards turn it off, so their own refusal is the answer.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_HostCallingAddValidation_RefusedInTheStandardWords(bool generated)
    {
        var (status, errors, ran) = await Send(generated, "api/v1/rules/abcd/5/ab", "owner=me", "X-Owner:me", validation: true);

        Assert.False(ran);
        Assert.Equal(422, status);
        Assert.Equal(["The field id must be a string with a maximum length of 3."], errors.Select(error => error.Detail));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_HostCallingAddValidationForABody_RefusedInTheStandardWords(bool generated)
    {
        var (status, errors, ran) = await Send(
            generated, "api/v1/rules", string.Empty, string.Empty, """[{"text":"a"},{"text":"b"},{"text":"c"}]""", validation: true);

        Assert.False(ran);
        Assert.Equal(422, status);
        Assert.Equal(["The field notes must be a string or array type with a maximum length of '2'."], errors.Select(error => error.Detail));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_HostCallingAddValidationAndAcceptsInvalidRequests_HandlerRuns(bool generated)
    {
        var (status, _, ran) = await Send(generated, "api/v1/lenient/abcd", "count=0", string.Empty, validation: true);

        Assert.True(ran);
        Assert.Equal(200, status);
    }

    // A blank string reaches the handler as none, or as its default, and a blank value of an array as null in its
    // place, as a controller binds them; a rule is then held to none.
    [Theory]
    [InlineData(true, "", "")]
    [InlineData(false, "", "")]
    [InlineData(true, "%20", " ")]
    [InlineData(false, "%20", " ")]
    public async Task Create_BlankValues_ReachTheHandlerAsNone(bool generated, string query, string header)
    {
        var (status, _, received) = await SendBlanks(
            generated,
            "api/v1/blanks/abc",
            $"required=x&optional={query}&ruled={query}&fallback={query}&many=a&many={query}",
            $"X-Required:x|X-Optional:{header}");

        Assert.Equal(200, status);
        Assert.Null(received!.Optional);
        Assert.Null(received.Ruled);
        Assert.Equal("default", received.Fallback);
        Assert.Equal(new string?[] { "a", null }, received.Many);
        Assert.Null(received.OptionalHeader);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_ValuesNotBlank_ReachTheHandlerAsSent(bool generated)
    {
        var (status, _, received) = await SendBlanks(
            generated, "api/v1/blanks/abc", "required=%20x%20&optional=a%20b&many=a&many=b", "X-Required: y |X-Optional:z");

        Assert.Equal(200, status);
        Assert.Equal("abc", received!.Id);
        Assert.Equal(" x ", received.Required);
        Assert.Equal(" y ", received.RequiredHeader);
        Assert.Equal("a b", received.Optional);
        Assert.Equal("default", received.Fallback);
        Assert.Equal(new string?[] { "a", "b" }, received.Many);
        Assert.Equal("z", received.OptionalHeader);
    }

    // A value that cannot be null sent blank is refused as required, as one not sent is, from every place.
    public static TheoryData<bool, string, string, string, string, string, bool> BlankRequired() => Cases(
    [
        ("abc", "required=", "X-Required:x", "required", "The required field is required.", true),
        ("abc", "required=%20%20", "X-Required:x", "required", "The required field is required.", true),
        ("abc", "", "X-Required:x", "required", "The required field is required.", true),
        ("abc", "required=x", "X-Required:", "X-Required", "The requiredHeader field is required.", false),
        ("abc", "required=x", "X-Required: ", "X-Required", "The requiredHeader field is required.", false),
        ("abc", "required=x", "", "X-Required", "The requiredHeader field is required.", false),
        (" ", "required=x", "X-Required:x", "id", "The id field is required.", false),
    ]);

    [Theory]
    [MemberData(nameof(BlankRequired))]
    public async Task Create_RequiredValueBlank_RefusedAsRequired(
        bool generated, string id, string query, string headers, string key, string message, bool queryParameter)
    {
        var (status, errors, received) = await SendBlanks(generated, $"api/v1/blanks/{id}", query, headers);

        Assert.Null(received);
        Assert.Equal(422, status);
        var error = Assert.Single(errors);
        Assert.Equal("422", error.Status);
        Assert.Equal(ErrorCodes.InvalidValue, error.Code);
        Assert.Equal(message, error.Detail);
        Assert.Equal(queryParameter ? key : null, error.Parameter);
        Assert.Equal(queryParameter ? null : RequestPointer.For(key), error.Pointer);
    }

    // A query value sent more than once is the first, as a controller binds and checks it, where the platform joins them.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_QueryValueSentTwice_TheFirstReachesTheHandler(bool generated)
    {
        var (status, _, received) = await SendBlanks(
            generated, "api/v1/blanks/abc", "required=x&required=y&ruled=abcd&ruled=efgh", "X-Required:x");

        Assert.Equal(200, status);
        Assert.Equal("x", received!.Required);
        Assert.Equal("abcd", received.Ruled);
    }

    [Theory]
    [InlineData(true, "required=x&ruled=abcdefgh&ruled=ab", "The field ruled must be a string with a minimum length of 2 and a maximum length of 5.")]
    [InlineData(false, "required=x&ruled=abcdefgh&ruled=ab", "The field ruled must be a string with a minimum length of 2 and a maximum length of 5.")]
    [InlineData(true, "required=&required=x", "The required field is required.")]
    [InlineData(false, "required=&required=x", "The required field is required.")]
    public async Task Create_QueryValueSentTwice_TheFirstChecked(bool generated, string query, string message)
    {
        var (status, errors, received) = await SendBlanks(generated, "api/v1/blanks/abc", query, "X-Required:x");

        Assert.Null(received);
        Assert.Equal(422, status);
        Assert.Equal([message], errors.Select(error => error.Detail));
    }

    // A handler that answers invalid requests itself receives a required value as sent, so it can name it, and an
    // optional one as none. Whitespace, since the request delegate generator can't bind an empty required value at all.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_AcceptsInvalidRequestsBlank_RequiredAsSentOptionalAsNone(bool generated)
    {
        var (status, _, received) = await SendBlanks(generated, "api/v1/blanks/lenient/ ", "required=%20&optional=%20", string.Empty);

        Assert.Equal(200, status);
        Assert.Equal(" ", received!.Id);
        Assert.Equal(" ", received.Required);
        Assert.Null(received.Optional);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_AcceptsInvalidRequestsSentTwice_TheFirstReachesTheHandler(bool generated)
    {
        var (status, _, received) = await SendBlanks(generated, "api/v1/blanks/lenient/abc", "required=x&required=y&optional=p&optional=q", string.Empty);

        Assert.Equal(200, status);
        Assert.Equal("x", received!.Required);
        Assert.Equal("p", received.Optional);
    }

    // The rule held to none is the parameter's own, which a value that is not blank must keep.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_RuledValueNotBlank_HeldToItsRule(bool generated)
    {
        var (status, errors, _) = await SendBlanks(generated, "api/v1/blanks/abc", "required=x&ruled=a", "X-Required:x");

        Assert.Equal(422, status);
        Assert.Equal(
            ["The field ruled must be a string with a minimum length of 2 and a maximum length of 5."],
            errors.Select(error => error.Detail));
    }

    /// <summary>
    /// Every case, with the request delegate generator and without.
    /// </summary>
    private static TheoryData<bool, string, string, string, string, string, bool> Cases(
        (string, string, string, string, string, bool)[] cases)
    {
        var data = new TheoryData<bool, string, string, string, string, string, bool>();

        foreach (var generated in new[] { true, false })
        {
            foreach (var (route, query, headers, key, message, queryParameter) in cases)
            {
                data.Add(generated, route, query, headers, key, message, queryParameter);
            }
        }

        return data;
    }

    private static TheoryData<bool, string, string, string> Cases((string, string, string)[] cases)
    {
        var data = new TheoryData<bool, string, string, string>();

        foreach (var generated in new[] { true, false })
        {
            foreach (var (route, query, headers) in cases)
            {
                data.Add(generated, route, query, headers);
            }
        }

        return data;
    }

    // Runtime(...) is a Delegate the request delegate generator can't resolve (RDG002), so the framework's runtime
    // factory builds that endpoint: each request runs on both of the framework's code paths.
#pragma warning disable RDG002
    /// <summary>
    /// Sends a request to the endpoint its path reaches, built by a host as the framework builds it, and says how it
    /// was answered and whether the handler ran.
    /// </summary>
    /// <param name="generated">Whether the request delegate generator builds the endpoint, or the runtime factory.</param>
    /// <param name="path">The path, without a leading slash.</param>
    /// <param name="query">The query string, without its <c>?</c>.</param>
    /// <param name="headers">Headers as <c>Name:value</c>, separated by <c>|</c>.</param>
    /// <param name="body">A JSON body, if any.</param>
    /// <param name="validation">Whether the host calls <c>AddValidation</c>.</param>
    private static async Task<(int Status, List<Failure> Errors, bool Ran)> Send(
        bool generated, string path, string query, string headers, string? body = null, bool validation = false)
    {
        var (status, errors, ran, _) = await SendAndRecord(generated, path, query, headers, body, validation);

        return (status, errors, ran);
    }

    /// <summary>
    /// Sends a request as <see cref="Send"/> does, and also says what a handler that records its arguments received.
    /// </summary>
    private static async Task<(int Status, List<Failure> Errors, bool Ran, Received.Values? Received)> SendAndRecord(
        bool generated, string path, string query, string headers, string? body = null, bool validation = false)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton<Ran>();
        builder.Services.AddSingleton<Received>();

        if (validation)
        {
            // Through a method group: the framework's validation generator handles only one AddValidation call in a
            // project, and StandardEndpointConventionTests makes it.
            Func<IServiceCollection, Action<Microsoft.Extensions.Validation.ValidationOptions>?, IServiceCollection> addValidation =
                ValidationServiceCollectionExtensions.AddValidation;
            addValidation(builder.Services, null);
        }
        await using var app = builder.Build();
        var rules = app.MapGroup("api/v1/rules").WithStandards();
        var lenient = app.MapGroup("api/v1/lenient").WithStandards();
        var blanks = app.MapGroup("api/v1/blanks").WithStandards();

        if (generated)
        {
            rules.MapGet("{id}/{number}/{slug}", Rules.ReadRule);
            rules.MapPost("", Rules.CreateRules);
            rules.MapPost("document", Rules.CreateDocument);
            rules.MapGet("model", Rules.ReadModel);
            rules.MapGet("bound", Rules.ReadBound);
            rules.MapPost("optional", Rules.CreateOptional);
            lenient.MapGet("{id}", LenientRules.ReadLenient);
            blanks.MapGet("{id}", Blanks.ReadBlanks);
            blanks.MapGet("lenient/{id}", Blanks.ReadLenientBlanks);
        }
        else
        {
            rules.MapGet("{id}/{number}/{slug}", Runtime(Rules.ReadRule));
            rules.MapPost("", Runtime(Rules.CreateRules));
            rules.MapPost("document", Runtime(Rules.CreateDocument));
            rules.MapGet("model", Runtime(Rules.ReadModel));
            rules.MapGet("bound", Runtime(Rules.ReadBound));
            rules.MapPost("optional", Runtime(Rules.CreateOptional));
            lenient.MapGet("{id}", Runtime(LenientRules.ReadLenient));
            blanks.MapGet("{id}", Runtime(Blanks.ReadBlanks));
            blanks.MapGet("lenient/{id}", Runtime(Blanks.ReadLenientBlanks));
        }

        await using var scope = app.Services.CreateAsyncScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Method = body is null ? HttpMethods.Get : HttpMethods.Post;
        http.Request.Path = "/" + path;
        http.Request.QueryString = query.Length == 0 ? QueryString.Empty : new QueryString("?" + query);
        http.Response.Body = new MemoryStream();

        foreach (var header in headers.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var (name, value) = (header[..header.IndexOf(':', StringComparison.Ordinal)], header[(header.IndexOf(':', StringComparison.Ordinal) + 1)..]);
            http.Request.Headers[name] = value;
        }

        if (body is not null)
        {
            http.Request.ContentType = "application/json";
            http.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));

            // A server says a request may have a body, and the framework reads none where nothing does.
            http.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature>(new HasBody());
        }

        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Single(candidate =>
                candidate.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(http.Request.Method) == true
                && new TemplateMatcher(TemplateParser.Parse(candidate.RoutePattern.RawText!), new RouteValueDictionary())
                    .TryMatch(http.Request.Path, http.Request.RouteValues));

        await endpoint.RequestDelegate!(http);

        return (http.Response.StatusCode, Failures(http), scope.ServiceProvider.GetRequiredService<Ran>().Value,
            scope.ServiceProvider.GetRequiredService<Received>().Arguments);
    }

    /// <summary>
    /// Sends a request to the handler that records what it received, and says how it was answered and what the
    /// handler received, or null when it did not run.
    /// </summary>
    private static async Task<(int Status, List<Failure> Errors, Received.Values? Received)> SendBlanks(
        bool generated, string path, string query, string headers)
    {
        var (status, errors, _, received) = await SendAndRecord(generated, path, query, headers);

        return (status, errors, received);
    }

    /// <summary>
    /// The handler as a <see cref="Delegate"/> the request delegate generator can't resolve, so the framework's runtime
    /// factory builds its endpoint.
    /// </summary>
    private static Delegate Runtime(Delegate handler) => handler;
#pragma warning restore RDG002

    private static List<Failure> Failures(HttpContext http)
    {
        http.Response.Body.Position = 0;

        if (http.Response.Body.Length == 0)
        {
            return [];
        }

        using var document = JsonDocument.Parse(http.Response.Body);

        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("errors", out var errors))
        {
            return [];
        }

        return
        [
            .. errors.EnumerateArray().Select(error => new Failure(
                Text(error, "status"),
                Text(error, "code"),
                Text(error, "title"),
                Text(error, "detail"),
                error.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object ? Text(source, "pointer") : null,
                error.TryGetProperty("source", out var named) && named.ValueKind == JsonValueKind.Object ? Text(named, "parameter") : null)),
        ];
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed class HasBody : Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }

    /// <summary>
    /// One error of the standard error document.
    /// </summary>
    private sealed record Failure(string? Status, string? Code, string? Title, string? Detail, string? Pointer, string? Parameter);

    /// <returns>Whether the handler ran.</returns>
    private static async Task<bool> Invoke(string handler, bool selfHosted)
    {
        var method = typeof(Handlers).GetMethod(handler)
            ?? typeof(SelfHostedHandlers).GetMethod(handler)
            ?? typeof(CloudHandlers).GetMethod(handler)!;
        var ran = false;
        var filter = StandardEndpointFilter.Create(
            new EndpointFilterFactoryContext { MethodInfo = method, ApplicationServices = new ServiceCollection().BuildServiceProvider() },
            _ =>
            {
                ran = true;
                return ValueTask.FromResult<object?>(null);
            });
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton<IBitwardenEnvironment>(new StubEnvironment(selfHosted))
                .BuildServiceProvider(),
        };

        await filter(new DefaultEndpointFilterInvocationContext(http));

        return ran;
    }

    private static DefaultHttpContext Created(string pathBase)
    {
        var http = new DefaultHttpContext();
        http.Request.PathBase = pathBase;
        http.Request.Path = "/api/v1/widgets/";

        StandardEndpointFilter.Succeeded(
            http, new ResourceDocument<Widget> { Data = new Widget { Id = "abc" } }, StatusCodes.Status201Created);

        return http;
    }

    public sealed record Widget : Resource;

    /// <summary>
    /// A body's item, with a rule of its own.
    /// </summary>
    public sealed record Note
    {
        /// <summary>
        /// Required, being non-nullable.
        /// </summary>
        public string Text { get; init; } = null!;
    }

    /// <summary>
    /// What the handler that records its arguments received.
    /// </summary>
    public sealed class Received
    {
        public Values? Arguments { get; set; }

        /// <summary>
        /// Its arguments.
        /// </summary>
        public sealed record Values(
            string Id,
            string Required,
            string RequiredHeader,
            string? Optional,
            string? Ruled,
            string Fallback,
            string?[]? Many,
            string? OptionalHeader);
    }

    /// <summary>
    /// A handler that records the strings it received, from every place a simple value is read.
    /// </summary>
    public static class Blanks
    {
        /// <summary>
        /// A read whose strings may be sent blank.
        /// </summary>
        [StandardReadOne]
        public static string ReadBlanks(
            string id,
            [FromQuery] string required,
            [FromHeader(Name = "X-Required")] string requiredHeader,
            [FromServices] Received received,
            [FromQuery] string? optional = null,
            [FromQuery][StringLength(5, MinimumLength = 2)] string? ruled = null,
            [FromQuery] string fallback = "default",
            [FromQuery] string?[]? many = null,
            [FromHeader(Name = "X-Optional")] string? optionalHeader = null)
        {
            received.Arguments = new Received.Values(id, required, requiredHeader, optional, ruled, fallback, many, optionalHeader);
            return "ok";
        }

        /// <summary>
        /// A read that answers invalid requests itself.
        /// </summary>
        [StandardReadOne]
        [AcceptsInvalidRequests]
        public static string ReadLenientBlanks(
            string id, [FromQuery] string required, [FromServices] Received received, [FromQuery] string? optional = null)
        {
            received.Arguments = new Received.Values(id, required, string.Empty, optional, null, string.Empty, null, null);
            return "ok";
        }
    }

    /// <summary>
    /// Whether a handler ran, for the request's scope.
    /// </summary>
    public sealed class Ran
    {
        public bool Value { get; set; }
    }

    /// <summary>
    /// Handlers whose parameters carry rules, from every place a simple value is read, and a body.
    /// </summary>
    public static class Rules
    {
        /// <summary>
        /// A read with a rule on every value.
        /// </summary>
        [StandardReadOne]
        public static string ReadRule(
            [StringLength(3)] string id,
            [Range(1, 100)] int number,
            [RegularExpression("^[a-z]+$")] string slug,
            [FromServices] Ran ran,
            [FromQuery][Required] string? owner = null,
            [FromQuery][Range(1, 100)] int count = 1,
            [FromQuery][StringLength(5)] string? label = null,
            [FromQuery(Name = "tag")][RegularExpression("^[a-z]+$")] string? tagged = null,
            [FromQuery][Range(1, 10)][Display(Name = "Page size")] int? size = null,
            [FromQuery][MinLength(2)] string[]? tags = null,
            [FromHeader(Name = "X-Owner")][Required] string? shelfOwner = null,
            [FromHeader(Name = "X-Count")][Range(1, 9)] int? shelfCount = null,
            [FromHeader(Name = "X-Name")][StringLength(5)] string? shelfName = null,
            [FromHeader(Name = "X-Tag")][RegularExpression("^[a-z]+$")] string? shelfTag = null)
        {
            ran.Value = true;
            return "ok";
        }

        /// <summary>
        /// A handler taking a short list.
        /// </summary>
        public static string CreateRules([MaxLength(2)] List<Note> notes, [FromServices] Ran ran, [FromQuery][Range(1, 5)] int? limit = null)
        {
            ran.Value = true;
            return "ok";
        }

        /// <summary>
        /// A handler whose body may be left out, but is required by its rule.
        /// </summary>
        public static string CreateOptional([Required] List<Note>? notes, [FromServices] Ran ran)
        {
            ran.Value = true;
            return "ok";
        }

        /// <summary>
        /// A create whose document carries a rule.
        /// </summary>
        public static string CreateDocument([Refuses] ResourceDocument<Widget> request, [FromServices] Ran ran)
        {
            ran.Value = true;
            return "ok";
        }

        /// <summary>
        /// A read whose query model carries a rule.
        /// </summary>
        public static string ReadModel([AsParameters][Refuses] Shelf query, [FromServices] Ran ran)
        {
            ran.Value = true;
            return "ok";
        }

        /// <summary>
        /// A read whose page carries a rule.
        /// </summary>
        public static string ReadBound([Refuses] Bitwarden.Server.Sdk.Resources.Querying.PageNumber pageNumber, [FromServices] Ran ran)
        {
            ran.Value = true;
            return "ok";
        }
    }

    /// <summary>
    /// Query values taken together.
    /// </summary>
    public sealed class Shelf
    {
        /// <summary>
        /// A label.
        /// </summary>
        [FromQuery]
        public string? Label { get; set; }
    }

    /// <summary>
    /// Refuses whatever it is given.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class RefusesAttribute() : ValidationAttribute("The {0} is refused.")
    {
        public override bool IsValid(object? value) => false;
    }

    /// <summary>
    /// A handler that answers invalid requests itself.
    /// </summary>
    public static class LenientRules
    {
        /// <summary>
        /// A read with rules nobody holds it to.
        /// </summary>
        [StandardReadOne]
        [AcceptsInvalidRequests]
        public static string ReadLenient([StringLength(3)] string id, [FromServices] Ran ran, [FromQuery][Range(1, 100)] int count = 1)
        {
            ran.Value = true;
            return "ok";
        }
    }

    private sealed class StubEnvironment(bool selfHosted) : IBitwardenEnvironment
    {
        public string Version => "1.0.0";

        public string? GitHash => null;

        public bool SelfHosted => selfHosted;

        public string? SelfHostFlavor => null;
    }

    private static class Handlers
    {
        [SelfHostedOnly]
        public static void SelfHostedOnly()
        {
        }

        [NotSelfHostedOnly]
        public static void NotSelfHostedOnly()
        {
        }

        public static void Anywhere()
        {
        }
    }

    [SelfHostedOnly]
    private static class SelfHostedHandlers
    {
        public static void SelfHostedByItsClass()
        {
        }
    }

    [NotSelfHostedOnly]
    private static class CloudHandlers
    {
        public static void NotSelfHostedByItsClass()
        {
        }
    }
}
