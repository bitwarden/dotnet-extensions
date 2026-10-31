using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;

namespace Bitwarden.Server.Sdk.Service.Tests;

/// <summary>
/// A service admits cross-origin requests from the browser origins it is configured with, and
/// from nowhere else.
/// </summary>
/// <remarks>
/// CORS is an access-control boundary: it decides which pages a browser lets read a service's
/// answers. So it is off unless configured, never admits any origin, and never admits
/// credentials. A preflight carries no token, so it must be answered before anything that
/// demands one.
/// </remarks>
public sealed class BrowserOriginsTests
{
    private const string WebVault = "http://web.localhost:8888";
    private const string Subdomain = "http://vault.localhost:8888";
    private const string Wildcard = "http://*.localhost:8888";
    private const string Elsewhere = "http://evil.example";
    private const string AuthenticatedPath = "/api/v1/probes/current";

    private static readonly Guid Person = new("a1000000-0000-4000-8000-00000000000a");

    [Fact]
    public async Task Preflight_ConfiguredOrigin_AllowsTheOriginMethodsAndHeaders()
    {
        await using var service = await Start(productService: false, WebVault);
        using var request = Preflight(WebVault, "PUT", "authorization", "content-type", "device-type", "bitwarden-client-name");

        using var response = await Send(service, request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(WebVault, Header(response, "Access-Control-Allow-Origin"));
        Assert.Equal(["delete", "get", "post", "put"], List(response, "Access-Control-Allow-Methods"));
        Assert.Equal(
            ["accept", "authorization", "bitwarden-client-name", "bitwarden-client-version", "bitwarden-package-type",
             "cache-control", "content-type", "device-identifier", "device-type", "is-prerelease", "pragma",
             "user-agent", "x-device-identifier", "x-requested-with"],
            List(response, "Access-Control-Allow-Headers"));
    }

    // Tokens travel in Authorization, which a page attaches on purpose. A cookie would be attached
    // by the browser on its own, for any page the policy admits.
    [Fact]
    public async Task Preflight_ConfiguredOrigin_NeverAllowsCredentials()
    {
        await using var service = await Start(productService: false, WebVault, Wildcard);
        using var request = Preflight(WebVault, "GET", "authorization");

        using var response = await Send(service, request);

        Assert.Equal(WebVault, Header(response, "Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    // A browser has no business asserting the acting user; leaving the header out of the policy
    // means its preflight fails before such a request is sent.
    [Fact]
    public async Task Preflight_ConfiguredOrigin_DoesNotAllowTheCallerContextHeaders()
    {
        await using var service = await Start(productService: false, WebVault);
        using var request = Preflight(WebVault, "GET", "bitwarden-user");

        using var response = await Send(service, request);

        Assert.DoesNotContain(BitwardenHeaders.User.ToLowerInvariant(), List(response, "Access-Control-Allow-Headers"));
        Assert.DoesNotContain(BitwardenHeaders.Organization.ToLowerInvariant(), List(response, "Access-Control-Allow-Headers"));
    }

    // A browser sends its token and nothing else about whose request it is, to a product service
    // as anywhere: none of the four is the browser's to send.
    [Theory]
    [InlineData("bitwarden-organization")]
    [InlineData("bitwarden-user")]
    [InlineData("bitwarden-provider")]
    [InlineData("bitwarden-context")]
    public async Task Preflight_ProductServiceWithACallerContextHeader_NotAllowed(string header)
    {
        await using var service = await Start(productService: true, WebVault);
        using var request = Preflight(WebVault, "GET", "authorization", header);

        using var response = await Send(service, request);

        Assert.DoesNotContain(header, List(response, "Access-Control-Allow-Headers"));
    }

    [Fact]
    public async Task Get_ProductServiceWithTheOrganizationHeader_Refusal400CarriesTheOrigin()
    {
        var organization = Guid.NewGuid();
        await using var service = await Start(productService: true, WebVault);
        var session = SessionHost.Token(
            Person, new Dictionary<string, object> { ["organization"] = organization.ToString() }, SessionHost.Scope);
        using var request = Get(WebVault, session);
        request.Headers.Add(BitwardenHeaders.Organization, organization.ToString());

        using var response = await Send(service, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(WebVault, Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Preflight_SubdomainWithAWildcardEntry_Allowed()
    {
        await using var service = await Start(productService: false, WebVault, Wildcard);
        using var request = Preflight(Subdomain, "POST", "content-type");

        using var response = await Send(service, request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(Subdomain, Header(response, "Access-Control-Allow-Origin"));
    }

    // An exact list stays exact: matching subdomains is switched on only by a wildcard entry.
    [Fact]
    public async Task Preflight_SubdomainWithoutAWildcardEntry_NoCorsHeaders()
    {
        await using var service = await Start(productService: false, WebVault);
        using var request = Preflight(Subdomain, "GET", "authorization");

        using var response = await Send(service, request);

        AssertNoCorsHeaders(response);
    }

    // A wildcard admits subdomains of its own host and port, not another host's that ends alike.
    [Theory]
    [InlineData("http://vault.localhost:9999")]
    [InlineData("https://vault.localhost:8888")]
    [InlineData("http://vault.notlocalhost:8888")]
    public async Task Preflight_OriginOutsideTheWildcard_NoCorsHeaders(string origin)
    {
        await using var service = await Start(productService: false, Wildcard);
        using var request = Preflight(origin, "GET", "authorization");

        using var response = await Send(service, request);

        AssertNoCorsHeaders(response);
    }

    [Fact]
    public async Task Preflight_UnlistedOrigin_NoCorsHeaders()
    {
        await using var service = await Start(productService: false, WebVault, Wildcard);
        using var request = Preflight(Elsewhere, "GET", "authorization");

        using var response = await Send(service, request);

        AssertNoCorsHeaders(response);
    }

    [Fact]
    public async Task Preflight_UnconfiguredService_NoCorsHeaders()
    {
        await using var service = await Start(productService: false);
        using var request = Preflight(WebVault, "GET", "authorization");

        using var response = await Send(service, request);

        AssertNoCorsHeaders(response);
    }

    [Fact]
    public async Task Get_UnconfiguredService_NoCorsHeaders()
    {
        await using var service = await Start(productService: false);
        using var request = Get(WebVault, SessionHost.Token(Person, SessionHost.Scope));

        using var response = await Send(service, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoCorsHeaders(response);
    }

    // The preflight carries no token. Answered after authentication it would be refused, and the
    // browser would never send the real request.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preflight_AuthenticatedEndpointWithoutAToken_Not401(bool productService)
    {
        await using var service = await Start(productService, WebVault);
        using var request = Preflight(WebVault, "GET", "authorization");

        using var response = await Send(service, request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(WebVault, Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Preflight_ProductService_AllowsTheWebVaultsHeaders()
    {
        await using var service = await Start(productService: true, WebVault, Wildcard);
        using var request = Preflight(
            WebVault, "DELETE", "authorization", "bitwarden-client-version", "bitwarden-package-type", "device-identifier");

        using var response = await Send(service, request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(WebVault, Header(response, "Access-Control-Allow-Origin"));
        Assert.Contains("delete", List(response, "Access-Control-Allow-Methods"));
    }

    [Fact]
    public async Task Get_ConfiguredOriginWithAToken_AnswerCarriesTheOrigin()
    {
        await using var service = await Start(productService: true, WebVault);
        using var request = Get(WebVault, SessionHost.Token(Person, SessionHost.Scope));

        using var response = await Send(service, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(WebVault, Header(response, "Access-Control-Allow-Origin"));
    }

    // The web vault refreshes its session on a 401, which it can only do if it may read it.
    [Fact]
    public async Task Get_ConfiguredOriginWithoutAToken_401CarriesTheOrigin()
    {
        await using var service = await Start(productService: true, WebVault);
        using var request = Get(WebVault, token: null);

        using var response = await Send(service, request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(WebVault, Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Get_ProductServiceWithTheUserHeader_Refusal400CarriesTheOrigin()
    {
        await using var service = await Start(productService: true, WebVault);
        using var request = Get(WebVault, SessionHost.Token(Person, SessionHost.Scope));
        request.Headers.Add(BitwardenHeaders.User, Person.ToString());

        using var response = await Send(service, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(WebVault, Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Get_UnlistedOrigin_AnsweredWithoutCorsHeaders()
    {
        await using var service = await Start(productService: true, WebVault);
        using var request = Get(Elsewhere, SessionHost.Token(Person, SessionHost.Scope));

        using var response = await Send(service, request);

        AssertNoCorsHeaders(response);
    }

    // Each would otherwise fail quietly, matching nothing or matching everything.
    [Theory]
    [InlineData("*")]
    [InlineData("")]
    [InlineData("http://web.localhost:8888/")]
    [InlineData("http://web.localhost:8888/vault")]
    [InlineData("web.localhost:8888")]
    [InlineData("ftp://web.localhost:8888")]
    [InlineData("http://web.*.localhost:8888")]
    [InlineData("http://*web.localhost:8888")]
    [InlineData("http://*.*.localhost:8888")]
    [InlineData("http://web.localhost:80")]
    [InlineData("http://user@web.localhost:8888")]
    public async Task Build_OriginABrowserNeverSends_RefusedAtStartup(string origin)
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Start(productService: false, origin));

        Assert.Contains("Service:BrowserOrigins", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_EmptyOriginList_NoCorsHeaders()
    {
        await using var service = await SessionHost.StartAsync(
            productService: false,
            new Dictionary<string, string?> { ["Service:BrowserOrigins"] = null });
        using var request = Preflight(WebVault, "GET", "authorization");

        using var response = await Send(service, request);

        AssertNoCorsHeaders(response);
    }

    [Fact]
    public async Task OpenApi_PublicIdentityUrlConfigured_TokenUrlIsItsTokenEndpoint()
    {
        await using var service = await SessionHost.StartAsync(
            productService: false,
            new Dictionary<string, string?>
            {
                ["Authentication:Schemes:Bearer:Authority"] = "http://localhost:5240",
                ["Service:PublicIdentityUrl"] = "http://identity.localhost:8888/",
            });

        Assert.Equal("http://identity.localhost:8888/connect/token", await TokenUrl(service));
    }

    [Fact]
    public async Task OpenApi_NoPublicIdentityUrl_TokenUrlIsTheAuthoritysAsBefore()
    {
        await using var service = await SessionHost.StartAsync(
            productService: false,
            new Dictionary<string, string?> { ["Authentication:Schemes:Bearer:Authority"] = "http://localhost:5240" });

        Assert.Equal("http://localhost:5240/connect/token", await TokenUrl(service));
    }

    [Fact]
    public async Task Build_PublicIdentityUrlNotAnHttpUrl_RefusedAtStartup()
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => SessionHost.StartAsync(
            productService: false,
            new Dictionary<string, string?> { ["Service:PublicIdentityUrl"] = "identity.localhost:8888" }));

        Assert.Contains("Service:PublicIdentityUrl", failure.Message, StringComparison.Ordinal);
    }

    private static Task<SessionHost> Start(bool productService, params string[] origins)
    {
        var configuration = new Dictionary<string, string?>();
        for (var i = 0; i < origins.Length; i++)
        {
            configuration[$"Service:BrowserOrigins:{i}"] = origins[i];
        }

        return SessionHost.StartAsync(productService, configuration);
    }

    private static HttpRequestMessage Preflight(string origin, string method, params string[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, AuthenticatedPath);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", string.Join(',', headers));
        return request;
    }

    private static HttpRequestMessage Get(string origin, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, AuthenticatedPath);
        request.Headers.Add("Origin", origin);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private static Task<HttpResponseMessage> Send(SessionHost service, HttpRequestMessage request) =>
        service.Client().SendAsync(request, TestContext.Current.CancellationToken);

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(',', values) : null;

    private static List<string> List(HttpResponseMessage response, string name) =>
        [.. (Header(response, name) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.ToLowerInvariant())
            .Order(StringComparer.Ordinal)];

    private static void AssertNoCorsHeaders(HttpResponseMessage response) =>
        Assert.DoesNotContain(
            response.Headers,
            header => header.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase));

    private static async Task<string?> TokenUrl(SessionHost service)
    {
        using var response = await service.Client().GetAsync("/api/openapi.json", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("service")
            .GetProperty("flows")
            .GetProperty("clientCredentials")
            .GetProperty("tokenUrl")
            .GetString();
    }
}
