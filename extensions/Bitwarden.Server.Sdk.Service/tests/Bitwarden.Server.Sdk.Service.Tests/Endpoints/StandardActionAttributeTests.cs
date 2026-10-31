using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// An action is published at <c>{resource}/actions/{action}</c>, where the resource is a member
/// of the collection unless the action names a singleton.
/// </summary>
public sealed class StandardActionAttributeTests
{
    private static readonly Guid Person = new("a1000000-0000-4000-8000-00000000000a");

    [Fact]
    public void Template_NoResourceGiven_IsAMemberOfTheCollection()
    {
        var actual = new StandardActionAttribute("SendEmail");

        Assert.Equal("{id}/actions/send-email", actual.Template);
        Assert.Equal("send-email", actual.Action);
    }

    [Fact]
    public void Template_SingletonGiven_IsTheSingletonsAction()
    {
        var actual = new StandardActionAttribute("change-kdf", template: "current");

        Assert.Equal("current/actions/change-kdf", actual.Template);
        Assert.Equal("change-kdf", actual.Action);
    }

    // The platform joins the two with a slash of its own, so a stray one cannot double it or
    // make the route absolute.
    [Fact]
    public void Template_SingletonWithSlashes_IsTrimmed()
    {
        var actual = new StandardActionAttribute("changeKdf", template: "/current/");

        Assert.Equal("current/actions/change-kdf", actual.Template);
    }

    // One conversion for every name the platform puts on the wire, so an action is named as a
    // resource type would be: an acronym is one word.
    [Theory]
    [InlineData("SendEmail", "send-email")]
    [InlineData("sendEmail", "send-email")]
    [InlineData("send-email", "send-email")]
    [InlineData("send-Email", "send-email")]
    [InlineData("VerifySSOToken", "verify-sso-token")]
    [InlineData("VerifySsoToken", "verify-sso-token")]
    [InlineData("Rotate2FA", "rotate2-fa")]
    public void Action_MethodName_KebabCasedAsAResourceTypeIs(string name, string action)
    {
        var single = new StandardActionAttribute(name);
        var bulk = new StandardBulkActionAttribute("api/v1/items", name);

        Assert.Equal(action, single.Action);
        Assert.Equal($"{{id}}/actions/{action}", single.Template);
        Assert.Equal(action, bulk.Action);
        Assert.Equal($"~/api/v1/items:bulk-{action}", bulk.Template);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Template_Blank_Refused(string template)
    {
        Assert.ThrowsAny<ArgumentException>(() => new StandardActionAttribute("mark", template));
    }

    [Fact]
    public async Task Post_SingletonAction_AnswersNoContent()
    {
        await using var service = await SessionHost.StartAsync(productService: true);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/probes/current/actions/mark")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", SessionHost.Token(Person, SessionHost.WriteScope));

        using var response = await service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_SingletonAction_PublishedAtTheSingletonsPath()
    {
        await using var service = await SessionHost.StartAsync(productService: true);

        using var response = await service.Client().GetAsync("/api/openapi.json", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var paths = document.RootElement.GetProperty("paths");
        var operation = paths.GetProperty("/api/v1/probes/current/actions/mark").GetProperty("post");
        Assert.Equal("mark", operation.GetProperty("operationId").GetString());
        Assert.True(operation.GetProperty("responses").TryGetProperty("204", out _));
    }
}
