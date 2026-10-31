using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A body an <see cref="AcceptsInvalidRequestsAttribute"/> handler's parameter can't be read from binds as
/// null, and the bytes the client sent stay in the request's <see cref="UnreadableRequestBody"/>, so the
/// handler can say why, as an MVC controller's model state does. A body that reads, and no body, leave none.
/// </summary>
public sealed class UnreadableRequestBodyTests(MinimalApiParityTests.Host host) : IClassFixture<MinimalApiParityTests.Host>
{
    [Theory]
    [InlineData("{nope", "{nope")]
    [InlineData("""{"subject":5}""", """{"subject":5}""")]
    [InlineData("""{"subject":"hi"}""", "read")]
    [InlineData("null", "nothing kept")]
    [InlineData("", "nothing kept")]
    public async Task AnUnreadableBody_KeptAsSent(string body, string expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/parity-raw/unread")
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)),
        };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", SessionHost.Token(null, SessionHost.Scope, SessionHost.WriteScope));

        using var response = await host.Service.Client().SendAsync(request, TestContext.Current.CancellationToken);
        var answer = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(200, (int)response.StatusCode);
        Assert.Equal(expected, JsonSerializer.Deserialize<string>(answer));
    }
}
