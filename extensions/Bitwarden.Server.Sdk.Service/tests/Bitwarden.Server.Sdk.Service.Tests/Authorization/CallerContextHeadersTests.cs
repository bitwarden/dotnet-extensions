using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// A domain service's caller context: believed only from another service, kept once per request as its context token
/// says, refused in the standard error shape pointing at the header, and taken from a service's own word about a person
/// only where the operation names that service.
/// </summary>
/// <remarks>
/// Verifying the context token itself is pinned in <see cref="CallerDelegationTests"/>.
/// </remarks>
public sealed class CallerContextHeadersTests : IAsyncLifetime
{
    private static readonly Guid Named = new("c4000000-0000-4000-8000-00000000000c");

    private SessionHost _domain = null!;

    public async ValueTask InitializeAsync() => _domain = await SessionHost.StartAsync(productService: false);

    public async ValueTask DisposeAsync() => await _domain.DisposeAsync();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static DefaultHttpContext Request() => new()
    {
        RequestServices = new ServiceCollection().BuildServiceProvider(),
        Response = { Body = new MemoryStream() },
    };

    private static VerifiedContext Verified() =>
        new(null, Named, null, new CallerContextValue { DeviceType = "9" }, "a-context-token", CallerContextToken.Delegated);

    // --- Who is believed -------------------------------------------------------------------------------------------

    // A person's token, or none: the headers are not read, and the request has no caller context.
    [Theory]
    [InlineData("web")]
    [InlineData(null)]
    public async Task Invoke_NotAnotherService_PassesOnWithNoContext(string? client)
    {
        var http = Request();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            client is null ? [] : [new Claim(InternalCaller.ClientClaim, client)], client is null ? null : "Bearer"));
        http.Request.Headers[BitwardenHeaders.User] = Named.ToString();
        http.Request.Headers[BitwardenHeaders.Delegation] = "not-a-context-token";
        var passed = false;

        await new CallerContextHeaders(_ => { passed = true; return Task.CompletedTask; }).InvokeAsync(http);

        Assert.True(passed);
        Assert.Null(CallerContextHeaders.Verified(http));
    }

    // --- What is kept ------------------------------------------------------------------------------------------------

    [Fact]
    public void Keep_IsWhatTheRestOfTheRequestReads()
    {
        var http = new DefaultHttpContext();
        var verified = Verified();

        CallerContextHeaders.Keep(http, verified);

        Assert.Same(verified, CallerContextHeaders.Verified(http));
        Assert.Same(verified.Context, CallerContextHeaders.Read(http));
    }

    [Fact]
    public void NothingKept_IsNoContext()
    {
        var http = new DefaultHttpContext();

        Assert.Null(CallerContextHeaders.Verified(http));
        Assert.Null(CallerContextHeaders.Read(http));
    }

    // --- How a call is refused ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest, ErrorCodes.InvalidRequest, ErrorTitles.InvalidRequest)]
    [InlineData(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthenticated, ErrorTitles.Unauthenticated)]
    [InlineData(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, ErrorTitles.Forbidden)]
    public async Task Refuse_IsTheStandardErrorPointingAtTheHeader(int status, string code, string title)
    {
        var http = Request();

        await CallerContextHeaders.RefuseAsync(http, BitwardenHeaders.Delegation, "Why it was refused.", status);

        Assert.Equal(status, http.Response.StatusCode);
        Assert.StartsWith("application/json", http.Response.ContentType, StringComparison.Ordinal);
        var error = await Error(http);
        Assert.Equal(status.ToString(System.Globalization.CultureInfo.InvariantCulture), error.GetProperty("status").GetString());
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.Equal(title, error.GetProperty("title").GetString());
        Assert.Equal("Why it was refused.", error.GetProperty("detail").GetString());
        Assert.Equal(BitwardenHeaders.Delegation, error.GetProperty("source").GetProperty("header").GetString());
    }

    [Fact]
    public async Task Refuse_ByDefault_Is400()
    {
        var http = Request();

        await CallerContextHeaders.RefuseAsync(http, BitwardenHeaders.Context, "Malformed.");

        Assert.Equal(StatusCodes.Status400BadRequest, http.Response.StatusCode);
        Assert.Equal(ErrorCodes.InvalidRequest, (await Error(http)).GetProperty("code").GetString());
    }

    // --- Delegated only, unless an operation says who may assert a person there --------------------------------------

    // A service's own word naming a person, at an operation acting for people: refused, whoever's credential it is.
    [Fact]
    public async Task Probe_AnAssertedContextNamingAPerson_Forbidden403()
    {
        var response = await Probe(SessionHost.SignedContext(user: Named, mode: CallerContextToken.Asserted));

        await AssertRefused(response, HttpStatusCode.Forbidden, BitwardenHeaders.Delegation);
    }

    // A further hop of an asserted context was admitted where it was first asserted: only a live call into a service
    // gives it a context token to pass on, so one credential alone can't have one signed.
    [Fact]
    public async Task Probe_AFurtherHopOfAnAssertedContextNamingAPerson_Accepted()
    {
        var response = await Probe(SessionHost.SignedContext(user: Named, mode: CallerContextToken.Asserted, hops: 2));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Named, await ProductServiceAttributeTests.ActingUser(response));
    }

    // With no act chain, or one that is not an object or ends in one that is not, the call counts one hop: an asserted person is refused.
    [Theory]
    [InlineData("none")]
    [InlineData("not-an-object")]
    [InlineData("ending-in-one-that-is-not-an-object")]
    public async Task Probe_AnAssertedContextNamingAPersonWithoutAnActorChain_CountsAsTheFirstHop(string kind)
    {
        object? actor = kind switch
        {
            "none" => null,
            "not-an-object" => "internal.first-service",
            _ => new Dictionary<string, object>
            {
                ["sub"] = "internal.first-service",
                [CallerContextToken.ActorClaim] = "internal.earlier-service",
            },
        };

        var response = await Probe(SessionHost.SignedContext(
            user: Named,
            mode: CallerContextToken.Asserted,
            claims: new Dictionary<string, object?> { [CallerContextToken.ActorClaim] = actor }));

        await AssertRefused(response, HttpStatusCode.Forbidden, BitwardenHeaders.Delegation);
    }

    // Naming nobody, it is the service's own call, bounded by its scopes.
    [Fact]
    public async Task Probe_AnAssertedContextNamingNobody_Accepted()
    {
        var response = await Probe(SessionHost.SignedContext(organization: Guid.NewGuid(), mode: CallerContextToken.Asserted));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // At an operation that names the caller as one that may assert a person, accepted; at one naming another, refused.
    [Theory]
    [InlineData("system-about-person", HttpStatusCode.OK)]
    [InlineData("another-system-about-person", HttpStatusCode.Forbidden)]
    public async Task Probe_AnAssertedContextNamingAPerson_OnlyWhereTheOperationAdmitsTheCaller(string probe, HttpStatusCode expected)
    {
        using var request = ProductServiceAttributeTests.Get($"/api/v1/probes/{probe}", SessionHost.Token(user: null, SessionHost.Scope));
        request.Headers.Add(BitwardenHeaders.Delegation, SessionHost.SignedContext(user: Named, mode: CallerContextToken.Asserted));

        using var response = await _domain.Client().SendAsync(request, Token);

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            Assert.Equal(Named, await ProductServiceAttributeTests.ActingUser(response));
        }
    }

    private async Task<HttpResponseMessage> Probe(string signed)
    {
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/current", SessionHost.Token(user: null, SessionHost.Scope));
        request.Headers.TryAddWithoutValidation(BitwardenHeaders.Delegation, signed);

        return await _domain.Client().SendAsync(request, Token);
    }

    private static async Task AssertRefused(HttpResponseMessage response, HttpStatusCode status, string header)
    {
        Assert.Equal(status, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var error = document.RootElement.GetProperty("errors")[0];
        Assert.Equal(header, error.GetProperty("source").GetProperty("header").GetString());
    }

    private static async Task<JsonElement> Error(HttpContext http)
    {
        http.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(http.Response.Body, cancellationToken: Token);
        return Assert.Single(document.RootElement.GetProperty("errors").EnumerateArray()).Clone();
    }
}
