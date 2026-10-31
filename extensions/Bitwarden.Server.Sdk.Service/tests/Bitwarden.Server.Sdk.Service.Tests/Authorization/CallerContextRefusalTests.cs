using System.Text.Json;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Common.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// A product service refuses a request carrying any caller-context header with <c>400</c>, pointing at the header,
/// before the request goes any further.
/// </summary>
public sealed class CallerContextRefusalTests
{
    private static DefaultHttpContext Request() => new()
    {
        RequestServices = new ServiceCollection().BuildServiceProvider(),
        Response = { Body = new MemoryStream() },
    };

    [Fact]
    public async Task Invoke_NoCallerContextHeader_PassesTheRequestOn()
    {
        var http = Request();
        http.Request.Headers["Device-Type"] = "9";
        var passed = false;

        await new CallerContextRefusal(_ => { passed = true; return Task.CompletedTask; }).InvokeAsync(http);

        Assert.True(passed);
        Assert.Equal(StatusCodes.Status200OK, http.Response.StatusCode);
    }

    public static TheoryData<string> Headers() => new(BitwardenHeaders.All);

    [Theory]
    [MemberData(nameof(Headers))]
    public async Task Invoke_ACallerContextHeader_Refused400PointingAtIt(string header)
    {
        var http = Request();
        http.Request.Headers[header] = "anything";

        await new CallerContextRefusal(_ => throw new InvalidOperationException("A refused request was passed on.")).InvokeAsync(http);

        Assert.Equal(StatusCodes.Status400BadRequest, http.Response.StatusCode);
        http.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(http.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        var error = Assert.Single(document.RootElement.GetProperty("errors").EnumerateArray());
        Assert.Equal(ErrorCodes.InvalidRequest, error.GetProperty("code").GetString());
        Assert.Equal(header, error.GetProperty("source").GetProperty("header").GetString());
        Assert.Equal(CallerContextRefusal.Detail(header), error.GetProperty("detail").GetString());
    }

    // One answer for a request carrying several: the first in the platform's list.
    [Fact]
    public async Task Invoke_SeveralCallerContextHeaders_PointsAtTheFirstListed()
    {
        var http = Request();
        http.Request.Headers[BitwardenHeaders.Provider] = "anything";
        http.Request.Headers[BitwardenHeaders.Organization] = "anything";

        await new CallerContextRefusal(_ => Task.CompletedTask).InvokeAsync(http);

        http.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(http.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            BitwardenHeaders.Organization,
            document.RootElement.GetProperty("errors")[0].GetProperty("source").GetProperty("header").GetString());
    }

    // The caller is told which header, and why this kind of service takes none of them.
    [Fact]
    public void Detail_NamesTheHeaderAndTheSession()
    {
        Assert.Equal(
            "The 'Bitwarden-User' header is not accepted by this service. It is a product service, called by people, "
            + "so whom a request concerns is what the session token names.",
            CallerContextRefusal.Detail(BitwardenHeaders.User));
    }
}
