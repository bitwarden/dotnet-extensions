using System.Text.Json;
using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// The standard error document written for a status that routing or content negotiation set
/// without a body.
/// </summary>
public sealed class StatusCodeErrorWriterTests
{
    [Fact]
    public async Task Write_MethodNotAllowed_NamesTheMethod()
    {
        var error = await Write(StatusCodes.Status405MethodNotAllowed, HttpMethods.Patch);

        Assert.Equal("405", error.GetProperty("status").GetString());
        Assert.Equal(ErrorCodes.MethodNotAllowed, error.GetProperty("code").GetString());
        Assert.Equal(ErrorTitles.MethodNotAllowed, error.GetProperty("title").GetString());
        Assert.Equal(ErrorDetails.MethodNotAllowed(HttpMethods.Patch), error.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Write_NotAcceptable_SaysWhatIsProduced()
    {
        var error = await Write(StatusCodes.Status406NotAcceptable, HttpMethods.Get);

        Assert.Equal("406", error.GetProperty("status").GetString());
        Assert.Equal(ErrorCodes.NotAcceptable, error.GetProperty("code").GetString());
        Assert.Equal(ErrorTitles.NotAcceptable, error.GetProperty("title").GetString());
        Assert.Equal(ErrorDetails.NotAcceptable, error.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Write_UnsupportedMediaType_SaysWhatIsRead()
    {
        var error = await Write(StatusCodes.Status415UnsupportedMediaType, HttpMethods.Post);

        Assert.Equal("415", error.GetProperty("status").GetString());
        Assert.Equal(ErrorCodes.UnsupportedMediaType, error.GetProperty("code").GetString());
        Assert.Equal(ErrorTitles.UnsupportedMediaType, error.GetProperty("title").GetString());
        Assert.Equal(ErrorDetails.UnsupportedMediaType, error.GetProperty("detail").GetString());
    }

    private static async Task<JsonElement> Write(int status, string method)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Response.StatusCode = status;
        http.Response.Body = new MemoryStream();

        await StatusCodeErrorWriter.Write(new StatusCodeContext(http, new StatusCodePagesOptions(), _ => Task.CompletedTask));

        Assert.Equal(status, http.Response.StatusCode);
        Assert.StartsWith("application/json", http.Response.ContentType, StringComparison.Ordinal);
        http.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(http.Response.Body, cancellationToken: TestContext.Current.CancellationToken);

        return Assert.Single(document.RootElement.GetProperty("errors").EnumerateArray()).Clone();
    }
}
