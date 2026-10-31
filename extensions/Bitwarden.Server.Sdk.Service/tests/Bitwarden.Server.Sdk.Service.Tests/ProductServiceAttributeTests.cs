using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;

namespace Bitwarden.Server.Sdk.Service.Tests;

/// <summary>
/// A product service takes the acting user from the person's session.
/// </summary>
/// <remarks>
/// The hole this closes: a domain service trusts an asserted <c>Bitwarden-User</c>,
/// which is safe only while no person can reach it. A service a person can reach must not trust
/// it, or anybody holding a session could name somebody else.
/// </remarks>
public sealed class ProductServiceAttributeTests : IAsyncLifetime
{
    private static readonly Guid PersonA = new("a1000000-0000-4000-8000-00000000000a");
    private static readonly Guid PersonB = new("b2000000-0000-4000-8000-00000000000b");

    private SessionHost _service = null!;

    public async ValueTask InitializeAsync() => _service = await SessionHost.StartAsync(productService: true);

    public async ValueTask DisposeAsync() => await _service.DisposeAsync();

    [Fact]
    public async Task GetProbe_TokenForAPlusHeaderNamingB_Refused400()
    {
        using var request = Get("/api/v1/probes/current", SessionHost.Token(PersonA, SessionHost.Scope));
        request.Headers.Add(BitwardenHeaders.User, PersonB.ToString());

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await FirstError(response);
        Assert.Equal("invalid-request", error.GetProperty("code").GetString());
        Assert.Contains(BitwardenHeaders.User, error.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(BitwardenHeaders.User, error.GetProperty("source").GetProperty("header").GetString());
    }

    // Naming yourself is refused too. The header is wrong here whoever it names, and accepting
    // it when it happens to agree would teach a caller that it is honoured.
    [Fact]
    public async Task GetProbe_HeaderNamingTheTokensOwnPerson_Refused400()
    {
        using var request = Get("/api/v1/probes/current", SessionHost.Token(PersonA, SessionHost.Scope));
        request.Headers.Add(BitwardenHeaders.User, PersonA.ToString());

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetProbe_TokenForA_ReachesTheHandlerAsA()
    {
        using var request = Get("/api/v1/probes/current", SessionHost.Token(PersonA, SessionHost.Scope));

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PersonA, await ActingUser(response));
    }

    // A service's own credential names no person. Here there would be nobody to act for.
    [Fact]
    public async Task GetProbe_TokenWithoutSub_Unauthenticated401()
    {
        using var request = Get("/api/v1/probes/current", SessionHost.Token(user: null, SessionHost.Scope));

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthenticated", (await FirstError(response)).GetProperty("code").GetString());
    }

    // The scope is the credential's, checked against the person's token as in any other service.
    [Fact]
    public async Task GetProbe_PersonsTokenWithoutTheScope_Forbidden403()
    {
        using var request = Get("/api/v1/probes/current", SessionHost.Token(PersonA, "something.else"));

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Ciphertext a few thousand characters long (the shape an EncString has), and well past the
    // 255 a plain string is held to. Never decrypted: it is not even real ciphertext.
    [Fact]
    public async Task CreateProbe_EncryptedStringOfThousandsOfCharacters_Accepted()
    {
        var secret = EncString(4_000);
        using var request = Post(
            "/api/v1/probes",
            SessionHost.Token(PersonA, SessionHost.WriteScope),
            new { data = new { type = "probe", attributes = new { secret } } });

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateProbe_EncryptedStringOverItsDeclaredBound_Refused422()
    {
        using var request = Post(
            "/api/v1/probes",
            SessionHost.Token(PersonA, SessionHost.WriteScope),
            new { data = new { type = "probe", attributes = new { secret = EncString(10_001) } } });

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // The default is what it protects, and is untouched for anything not declared encrypted.
    [Fact]
    public async Task CreateProbe_PlainStringOver255_Refused422()
    {
        using var request = Post(
            "/api/v1/probes",
            SessionHost.Token(PersonA, SessionHost.WriteScope),
            new { data = new { type = "probe", attributes = new { note = new string('n', 256) } } });

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_EncryptedMember_ShowsItsDeclaredMaxLength()
    {
        var attributes = await ProbeAttributes();

        Assert.Equal(10_000, attributes.GetProperty("secret").GetProperty("maxLength").GetInt32());
    }

    [Fact]
    public async Task OpenApi_PlainMember_StillShowsTheDefaultMaxLength()
    {
        var attributes = await ProbeAttributes();

        Assert.Equal(255, attributes.GetProperty("note").GetProperty("maxLength").GetInt32());
    }

    // Not offered as credentials where they are refused.
    [Theory]
    [InlineData("organization")]
    [InlineData("user")]
    [InlineData("provider")]
    [InlineData("context")]
    public async Task OpenApi_ProductService_DoesNotOfferACallerContextHeader(string scheme)
    {
        using var document = await OpenApi(_service);

        var schemes = document.RootElement.GetProperty("components").GetProperty("securitySchemes");
        Assert.False(schemes.TryGetProperty(scheme, out _));
    }

    // A generated client decides from the document whether to send a token at all.
    [Fact]
    public async Task OpenApi_ProductService_DeclaresTheSessionToken()
    {
        using var document = await OpenApi(_service);

        var schemes = document.RootElement.GetProperty("components").GetProperty("securitySchemes");
        var session = schemes.GetProperty("session");
        Assert.Equal("http", session.GetProperty("type").GetString());
        Assert.Equal("bearer", session.GetProperty("scheme").GetString());
    }

    [Fact]
    public void Identity_ProductService_SaysSo()
    {
        Assert.True(_service.Identity.IsProductService);
    }

    // Inherited, because losing it is the unsafe direction: a derived type that did not inherit
    // it would believe the header this test sends.
    [Fact]
    public async Task GetProbe_DerivedFromAProductServiceWithoutTheAttribute_StillRefusesTheHeader400()
    {
        await using var derived = await SessionHost.StartDerivedFromProductServiceAsync();
        using var request = Get("/api/v1/probes/current", SessionHost.Token(PersonA, SessionHost.Scope));
        request.Headers.Add(BitwardenHeaders.User, PersonB.ToString());

        using var response = await derived.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.True(derived.Identity.IsProductService);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    internal static HttpRequestMessage Get(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static HttpRequestMessage Post(string path, string token, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    /// <summary>
    /// Something shaped like an AES-CBC-HMAC EncString, of the length asked for.
    /// </summary>
    private static string EncString(int length)
    {
        const string prefix = "2.";
        var body = new StringBuilder(prefix, length);
        var alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

        while (body.Length < length)
        {
            body.Append(body.Length % 97 == 0 ? '|' : alphabet[body.Length % alphabet.Length]);
        }

        return body.ToString();
    }

    internal static async Task<Guid?> ActingUser(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // A resource whose attributes are all absent is written without an attributes object.
        return document.RootElement.GetProperty("data").TryGetProperty("attributes", out var attributes)
            && attributes.TryGetProperty("actingUser", out var user)
                ? user.GetGuid()
                : null;
    }

    private static async Task<JsonElement> FirstError(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return document.RootElement.GetProperty("errors")[0].Clone();
    }

    internal static async Task<JsonDocument> OpenApi(SessionHost service) =>
        JsonDocument.Parse(await service.Client().GetStringAsync(
            "/api/openapi.json", TestContext.Current.CancellationToken));

    /// <summary>
    /// The probe's attributes as the published document describes them.
    /// </summary>
    private async Task<JsonElement> ProbeAttributes()
    {
        using var document = await OpenApi(_service);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        // Whichever schema describes the probe's attributes; its name is the platform's business.
        foreach (var schema in schemas.EnumerateObject())
        {
            if (schema.Value.TryGetProperty("properties", out var properties)
                && properties.TryGetProperty("secret", out _)
                && properties.TryGetProperty("note", out _)
                && properties.TryGetProperty("actingUser", out _))
            {
                return properties.Clone();
            }
        }

        throw new InvalidOperationException("No schema in the document describes the probe's attributes.");
    }
}
