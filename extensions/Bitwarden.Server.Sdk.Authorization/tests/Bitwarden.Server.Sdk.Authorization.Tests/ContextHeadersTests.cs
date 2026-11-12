using System.Security.Claims;
using System.Text.Json;
using Bitwarden.Server.Sdk.Context;
using Bitwarden.Server.Sdk.Errors;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Authorization.Tests;

/// <summary>
/// Reads a domain service's context from the call's context token once, before the operation runs: a call it refuses
/// goes no further, an asserted context naming a person is refused unless the operation admits its caller, and one it
/// accepts is kept for the rest of the request.
/// </summary>
public sealed class ContextHeadersTests
{
    private static readonly Guid Organization = new("0c000000-0000-4000-8000-00000000000c");
    private static readonly Guid User = new("a1000000-0000-4000-8000-00000000000a");

    private static void Operation(HttpContext http, params object[] metadata) =>
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata), "GetWidget"));

    private static async Task<bool> Invoke(HttpContext http)
    {
        var passed = false;
        await new ContextHeaders(_ =>
        {
            passed = true;
            return Task.CompletedTask;
        }).InvokeAsync(http);
        return passed;
    }

    private static async Task<JsonElement> Error(HttpContext http)
    {
        http.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(http.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        return Assert.Single(document.RootElement.GetProperty("errors").EnumerateArray()).Clone();
    }

    [Fact]
    public async Task Invoke_ADelegatedContext_KeptAndPassedOn()
    {
        var http = ContextTokens.Request();
        var token = ContextTokens.Sign(Organization, User);
        http.Request.Headers[BitwardenHeaders.Delegation] = token;

        Assert.True(await Invoke(http));

        var kept = Assert.IsType<VerifiedContext>(ContextHeaders.Verified(http));
        Assert.Equal(Organization, kept.Organization);
        Assert.Equal(User, kept.User);
        Assert.Equal(token, kept.Token);
        Assert.Equal(StatusCodes.Status200OK, http.Response.StatusCode);
    }

    [Fact]
    public async Task Invoke_NoContextAtAll_PassedOnWithNothingKept()
    {
        var http = ContextTokens.Request();

        Assert.True(await Invoke(http));
        Assert.Null(ContextHeaders.Verified(http));
    }

    // From anybody but another service the headers are ignored, not refused: Identity's sign-in surface is called by
    // browsers, and what they assert is worth nothing.
    [Theory]
    [InlineData("web")]
    [InlineData("internal.")]
    [InlineData(null)]
    public async Task Invoke_NotAnotherService_PassedOnWithNothingKept(string? client)
    {
        var http = ContextTokens.Request();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            client is null ? [] : [new Claim(InternalCaller.ClientClaim, client)],
            client is null ? null : "Bearer"));
        http.Request.Headers[BitwardenHeaders.Delegation] = ContextTokens.Sign(Organization, User);
        http.Request.Headers[BitwardenHeaders.User] = User.ToString();

        Assert.True(await Invoke(http));
        Assert.Null(ContextHeaders.Verified(http));
    }

    [Fact]
    public async Task Invoke_AContextTokenThisServiceDoesntAccept_Unauthenticated401()
    {
        var http = ContextTokens.Request();
        http.Request.Headers[BitwardenHeaders.Delegation] = ContextTokens.Sign(user: User, audience: "gadget-service");

        Assert.False(await Invoke(http));

        Assert.Equal(StatusCodes.Status401Unauthorized, http.Response.StatusCode);
        Assert.Null(ContextHeaders.Verified(http));
        var error = await Error(http);
        Assert.Equal(ErrorCodes.Unauthenticated, error.GetProperty("code").GetString());
        Assert.Equal(BitwardenHeaders.Delegation, error.GetProperty("source").GetProperty("header").GetString());
    }

    [Fact]
    public async Task Invoke_APlainHeaderDisagreeingWithTheToken_Refused400()
    {
        var http = ContextTokens.Request();
        http.Request.Headers[BitwardenHeaders.Delegation] = ContextTokens.Sign(Organization, User);
        http.Request.Headers[BitwardenHeaders.Organization] = Guid.NewGuid().ToString();

        Assert.False(await Invoke(http));

        Assert.Equal(StatusCodes.Status400BadRequest, http.Response.StatusCode);
        var error = await Error(http);
        Assert.Equal(ErrorCodes.InvalidRequest, error.GetProperty("code").GetString());
        Assert.Equal(BitwardenHeaders.Organization, error.GetProperty("source").GetProperty("header").GetString());
    }

    // Any holder of a service credential can have an asserted context signed naming anybody, so naming a person it is
    // refused where the operation doesn't say otherwise.
    [Fact]
    public async Task Invoke_AnAssertedContextNamingAPerson_Forbidden403()
    {
        var http = ContextTokens.Request();
        Operation(http);
        http.Request.Headers[BitwardenHeaders.Delegation] = ContextTokens.Sign(user: User, mode: ContextToken.Asserted);

        Assert.False(await Invoke(http));

        Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
        Assert.Null(ContextHeaders.Verified(http));
        var error = await Error(http);
        Assert.Equal(ErrorCodes.Forbidden, error.GetProperty("code").GetString());
        Assert.Equal(BitwardenHeaders.Delegation, error.GetProperty("source").GetProperty("header").GetString());
    }

    [Fact]
    public async Task Invoke_AnAssertedContextNamingAPersonWithNoEndpoint_Forbidden403()
    {
        var http = ContextTokens.Request();
        http.Request.Headers[BitwardenHeaders.Delegation] = ContextTokens.Sign(user: User, mode: ContextToken.Asserted);

        Assert.False(await Invoke(http));
        Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
    }

    [Fact]
    public async Task Invoke_AnAssertedContextNamingAPerson_AcceptedWhereTheOperationAdmitsTheCaller()
    {
        var http = ContextTokens.Request();
        Operation(http, new AcceptsAssertedContextAttribute("Background work about a person.", "gadget-service"));
        http.Request.Headers[BitwardenHeaders.Delegation] = ContextTokens.Sign(user: User, mode: ContextToken.Asserted);

        Assert.True(await Invoke(http));
        Assert.Equal(User, ContextHeaders.Verified(http)?.User);
    }

    // A named exception for one service is no exception for another.
    [Fact]
    public async Task Invoke_AnAssertedContextNamingAPerson_ForbiddenWhereTheOperationAdmitsOnlyOthers()
    {
        var http = ContextTokens.Request();
        Operation(http, new AcceptsAssertedContextAttribute("Background work about a person.", "widget-service"));
        http.Request.Headers[BitwardenHeaders.Delegation] = ContextTokens.Sign(user: User, mode: ContextToken.Asserted);

        Assert.False(await Invoke(http));
        Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
    }

    // A further hop of an asserted context was admitted where it was first asserted.
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Invoke_AFurtherHopOfAnAssertedContextNamingAPerson_Accepted(int hops)
    {
        var http = ContextTokens.Request();
        Operation(http);
        http.Request.Headers[BitwardenHeaders.Delegation] =
            ContextTokens.Sign(user: User, mode: ContextToken.Asserted, hops: hops);

        Assert.True(await Invoke(http));
        Assert.Equal(hops, ContextHeaders.Verified(http)?.Hops);
    }

    // The service's own call, about an organization or nothing, is bounded by the operation's scopes already.
    [Fact]
    public async Task Invoke_AnAssertedContextNamingNobody_Accepted()
    {
        var http = ContextTokens.Request();
        Operation(http);
        http.Request.Headers[BitwardenHeaders.Delegation] = ContextTokens.Sign(Organization, mode: ContextToken.Asserted);

        Assert.True(await Invoke(http));
        Assert.Equal(Organization, ContextHeaders.Verified(http)?.Organization);
    }

    [Fact]
    public void Keep_IsWhatTheRestOfTheRequestReads()
    {
        var http = new DefaultHttpContext();
        var context = new ContextValue { DeviceType = "9" };
        var verified = new VerifiedContext(Organization, User, null, context, "a-context-token", ContextToken.Delegated);

        ContextHeaders.Keep(http, verified);

        Assert.Same(verified, ContextHeaders.Verified(http));
        Assert.Same(context, ContextHeaders.Read(http));
    }

    [Fact]
    public void Read_NothingKept_IsNull()
    {
        var http = new DefaultHttpContext();

        Assert.Null(ContextHeaders.Verified(http));
        Assert.Null(ContextHeaders.Read(http));
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest, ErrorCodes.InvalidRequest, ErrorTitles.InvalidRequest)]
    [InlineData(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthenticated, ErrorTitles.Unauthenticated)]
    [InlineData(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, ErrorTitles.Forbidden)]
    public async Task Refuse_IsTheStandardErrorPointingAtTheHeader(int status, string code, string title)
    {
        var http = ContextTokens.Request();

        await ContextHeaders.RefuseAsync(http, BitwardenHeaders.User, "Not this one.", status);

        Assert.Equal(status, http.Response.StatusCode);
        Assert.StartsWith("application/json", http.Response.ContentType);
        var error = await Error(http);
        Assert.Equal(status.ToString(System.Globalization.CultureInfo.InvariantCulture), error.GetProperty("status").GetString());
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.Equal(title, error.GetProperty("title").GetString());
        Assert.Equal("Not this one.", error.GetProperty("detail").GetString());
        Assert.Equal(BitwardenHeaders.User, error.GetProperty("source").GetProperty("header").GetString());
    }

    [Fact]
    public async Task Refuse_ByDefault_Is400()
    {
        var http = ContextTokens.Request();

        await ContextHeaders.RefuseAsync(http, BitwardenHeaders.Context, "Not this one.");

        Assert.Equal(StatusCodes.Status400BadRequest, http.Response.StatusCode);
    }
}
