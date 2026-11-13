using System.Net;
using System.Text.Json;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// [AcceptsSendCredential]: a token minted for one Send's link, naming no person, is accepted by the
/// operations that declare it and refused by every other.
/// </summary>
public sealed class SendCredentialTests : IAsyncLifetime
{
    private const string Send = "5e4d0000-0000-4000-8000-000000000001";
    private static readonly Guid Person = new("a1000000-0000-4000-8000-00000000000a");

    private SessionHost _service = null!;

    public async ValueTask InitializeAsync() => _service = await SessionHost.StartAsync(productService: true);

    public async ValueTask DisposeAsync() => await _service.DisposeAsync();

    private static string SendToken(string? send, params string[] scopes)
    {
        var claims = new Dictionary<string, object> { ["sub"] = $"send:{send}" };
        if (send is not null)
        {
            claims["send_id"] = send;
        }

        return SessionHost.Token(null, claims, scopes);
    }

    private async Task<HttpResponseMessage> Get(string path, string token) =>
        await _service.Client().SendAsync(ProductServiceAttributeTests.Get(path, token), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Get_SendCredentialOnAnOperationThatAcceptsIt_TheSendAndNobodyActing()
    {
        using var response = await Get("/api/v1/probes/send", SendToken(Send, SessionHost.SendScope));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var attributes = document.RootElement.GetProperty("data").GetProperty("attributes");
        Assert.Equal(Send, attributes.GetProperty("note").GetString());
        Assert.False(attributes.TryGetProperty("actingUser", out var acting) && acting.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_SendCredentialOnAnyOtherOperation_Unauthorized()
    {
        using var response = await Get("/api/v1/probes/current", SendToken(Send, SessionHost.Scope));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_SendCredentialWithoutTheScope_Forbidden()
    {
        using var response = await Get("/api/v1/probes/send", SendToken(Send, SessionHost.Scope));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // A token naming no person and no Send is still nobody's.
    [Fact]
    public async Task Get_TokenNamingNeitherPersonNorSend_Unauthorized()
    {
        using var response = await Get("/api/v1/probes/send", SendToken(null, SessionHost.SendScope));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // A person's session is refused: it names a user, which the operation forbids.
    [Fact]
    public async Task Get_PersonsSession_Forbidden()
    {
        using var response = await Get("/api/v1/probes/send", SessionHost.Token(Person, SessionHost.SendScope));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
