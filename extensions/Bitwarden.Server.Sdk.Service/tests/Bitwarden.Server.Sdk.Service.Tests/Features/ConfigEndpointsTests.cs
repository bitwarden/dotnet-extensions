using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Features;
using Bitwarden.Server.Sdk.Service.Features;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using LdContext = LaunchDarkly.Sdk.Context;

namespace Bitwarden.Server.Sdk.Service.Tests.Features;

/// <summary>
/// Every product service serves <c>GET /api/v1/config</c>; no domain service does.
/// </summary>
/// <remarks>
/// No test here sets <c>Features:LaunchDarkly:SdkKey</c>, so every flag is served from
/// <c>Features:FlagValues</c> through LaunchDarkly's offline test data, and nothing leaves the
/// process.
/// </remarks>
public sealed class ConfigEndpointsTests
{
    private const string Path = "/api/v1/config";

    private static readonly Guid Person = new("c3000000-0000-4000-8000-00000000000c");

    private static readonly Dictionary<string, string?> Configured = new()
    {
        ["Service:Environment:CloudRegion"] = "US",
        ["Service:Environment:Vault"] = "http://web.localhost:8888",
        ["Service:Environment:FillAssistRules"] = "https://rules.example/fill-assist.json",
        ["Service:Settings:DisableUserRegistration"] = "true",
        ["Service:Settings:SuppressOnboardingInterstitials"] = "false",
        ["Features:KnownFlags:0"] = "bool-flag",
        ["Features:KnownFlags:1"] = "int-flag",
        ["Features:KnownFlags:2"] = "string-flag",
        ["Features:KnownFlags:3"] = "known-without-a-value",
        ["Features:FlagValues:bool-flag"] = "true",
        ["Features:FlagValues:int-flag"] = "7",
        ["Features:FlagValues:string-flag"] = "teal",
        ["Features:FlagValues:unknown-flag"] = "true",
    };

    [Fact]
    public async Task GetConfig_Anonymous_ReturnsTheConfigResource()
    {
        await using var service = await SessionHost.StartAsync(productService: true, Configured);

        using var response = await service.Client().GetAsync(Path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await Json(response);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("config", data.GetProperty("type").GetString());
        Assert.False(data.TryGetProperty("id", out _));

        var attributes = data.GetProperty("attributes");
        Assert.False(string.IsNullOrEmpty(attributes.GetProperty("version").GetString()));

        var environment = attributes.GetProperty("environment");
        Assert.Equal("US", environment.GetProperty("cloudRegion").GetString());
        Assert.Equal("http://web.localhost:8888", environment.GetProperty("vault").GetString());
        Assert.Equal("https://rules.example/fill-assist.json", environment.GetProperty("fillAssistRules").GetString());

        var settings = attributes.GetProperty("settings");
        Assert.True(settings.GetProperty("disableUserRegistration").GetBoolean());
        Assert.False(settings.GetProperty("suppressOnboardingInterstitials").GetBoolean());
    }

    // Typed as they are configured, and only the known ones: a value with no known key is not
    // published, and a known key with no value has nothing to say.
    [Fact]
    public async Task GetConfig_NoSdkKey_ServesKnownFlagsFromFlagValues()
    {
        await using var service = await SessionHost.StartAsync(productService: true, Configured);

        using var response = await service.Client().GetAsync(Path, TestContext.Current.CancellationToken);

        using var document = await Json(response);
        var flags = document.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("featureFlags");
        Assert.True(flags.GetProperty("bool-flag").GetBoolean());
        Assert.Equal(7, flags.GetProperty("int-flag").GetInt32());
        Assert.Equal("teal", flags.GetProperty("string-flag").GetString());
        Assert.False(flags.TryGetProperty("unknown-flag", out _));
        Assert.False(flags.TryGetProperty("known-without-a-value", out _));
    }

    // Nothing configured is still a valid answer: empty environment, settings off, no flags.
    [Fact]
    public async Task GetConfig_NothingConfigured_ReturnsDefaults()
    {
        await using var service = await SessionHost.StartAsync(productService: true);

        using var response = await service.Client().GetAsync(Path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await Json(response);
        var attributes = document.RootElement.GetProperty("data").GetProperty("attributes");
        Assert.Empty(attributes.GetProperty("environment").EnumerateObject());
        Assert.False(attributes.GetProperty("settings").GetProperty("disableUserRegistration").GetBoolean());
        Assert.False(attributes.GetProperty("settings").GetProperty("selfHosted").GetBoolean());
        Assert.Empty(attributes.GetProperty("featureFlags").EnumerateObject());
    }

    // A self-hosted install says so, so a client can tell which kind of install it reached.
    [Fact]
    public async Task GetConfig_SelfHosted_SaysSo()
    {
        await using var service = await SessionHost.StartAsync(productService: true,
            new Dictionary<string, string?> { ["globalSettings:selfHosted"] = "true" });

        using var response = await service.Client().GetAsync(Path, TestContext.Current.CancellationToken);

        using var document = await Json(response);
        Assert.True(document.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("settings")
            .GetProperty("selfHosted").GetBoolean());
    }

    [Fact]
    public async Task GetConfig_Anonymous_EvaluatesForTheAnonymousUser()
    {
        var spy = new SpyContextBuilder();
        await using var service = await StartWithSpy(spy);
        using var request = new HttpRequestMessage(HttpMethod.Get, Path);
        request.Headers.Add("Device-Identifier", "device-1");
        request.Headers.Add("Device-Type", "9");
        request.Headers.Add("Bitwarden-Client-Version", "2026.9.1");

        using var response = await service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var built = Assert.Single(spy.Built);
        Assert.True(built.Multiple);
        Assert.True(built.TryGetContextByKind(LaunchDarkly.Sdk.ContextKind.Default, out var user));
        Assert.Equal(ServiceContextBuilder.AnonymousKey, user.Key);
        Assert.True(user.Anonymous);
        Assert.True(built.TryGetContextByKind(LaunchDarkly.Sdk.ContextKind.Of("device"), out var device));
        Assert.Equal("device-1", device.Key);
    }

    [Fact]
    public async Task GetConfig_SessionToken_EvaluatesForThePersonAndTheirDevice()
    {
        var spy = new SpyContextBuilder();
        await using var service = await StartWithSpy(spy);
        using var request = new HttpRequestMessage(HttpMethod.Get, Path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            SessionHost.Token(Person, new Dictionary<string, object> { ["device"] = "session-device" }, SessionHost.Scope));
        request.Headers.Add("Device-Identifier", "header-device");

        using var response = await service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var built = Assert.Single(spy.Built);
        Assert.True(built.TryGetContextByKind(LaunchDarkly.Sdk.ContextKind.Default, out var user));
        Assert.Equal(Person.ToString(), user.Key);
        Assert.False(user.Anonymous);

        // The token's device first, then the request's header.
        Assert.True(built.TryGetContextByKind(LaunchDarkly.Sdk.ContextKind.Of("device"), out var device));
        Assert.Equal("session-device", device.Key);
    }

    // The scope belongs to the API, not to a document anyone may read: a token without it still
    // targets the person rather than being refused.
    [Fact]
    public async Task GetConfig_SessionTokenWithoutTheReadScope_StillAnswered()
    {
        await using var service = await SessionHost.StartAsync(productService: true, Configured);
        using var request = new HttpRequestMessage(HttpMethod.Get, Path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SessionHost.Token(Person, "something.else"));

        using var response = await service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // The refusal applies here as everywhere on a product service.
    [Fact]
    public async Task GetConfig_AssertedUserHeader_Refused400()
    {
        await using var service = await SessionHost.StartAsync(productService: true, Configured);
        using var request = new HttpRequestMessage(HttpMethod.Get, Path);
        request.Headers.Add(BitwardenHeaders.User, Person.ToString());

        using var response = await service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetConfig_DomainService_NotFound()
    {
        await using var service = await SessionHost.StartAsync(productService: false, Configured);
        using var request = new HttpRequestMessage(HttpMethod.Get, Path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SessionHost.Token(null, SessionHost.Scope));

        using var response = await service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_ProductService_PublishesTheAnonymousConfigRead()
    {
        await using var service = await SessionHost.StartAsync(productService: true);

        using var document = await OpenApi(service);

        var operation = document.RootElement.GetProperty("paths").GetProperty(Path).GetProperty("get");
        Assert.Equal("Config", operation.GetProperty("tags")[0].GetString());

        // Anonymous: no security requirement, or an empty one, which says the same.
        if (operation.TryGetProperty("security", out var security))
        {
            Assert.All(security.EnumerateArray(), requirement => Assert.Empty(requirement.EnumerateObject()));
        }

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("ConfigDocument", out _));
        var attributes = schemas.GetProperty("Config").GetProperty("properties").GetProperty("attributes");
        var properties = Resolve(schemas, attributes).GetProperty("properties");
        foreach (var name in (string[])["version", "environment", "settings", "featureFlags"])
        {
            Assert.True(properties.TryGetProperty(name, out _), $"attributes.{name} is not published.");
        }

        // A map, so a generated client has somewhere to put the flags.
        Assert.True(properties.GetProperty("featureFlags").TryGetProperty("additionalProperties", out _));
    }

    [Fact]
    public async Task OpenApi_DomainService_DoesNotPublishConfig()
    {
        await using var service = await SessionHost.StartAsync(productService: false);

        using var document = await OpenApi(service);

        Assert.False(document.RootElement.GetProperty("paths").TryGetProperty(Path, out _));
    }

    private static Task<SessionHost> StartWithSpy(SpyContextBuilder spy) =>
        SessionHost.StartAsync(
            productService: true,
            Configured,
            services => services.AddSingleton<IContextBuilder>(provider =>
            {
                spy.Inner = new ServiceContextBuilder(
                    provider.GetRequiredService<IHttpContextAccessor>(),
                    provider.GetRequiredService<IContext>());
                return spy;
            }));

    private static JsonElement Resolve(JsonElement schemas, JsonElement schema) =>
        schema.TryGetProperty("$ref", out var reference)
            ? schemas.GetProperty(reference.GetString()!.Split('/')[^1])
            : schema;

    private static async Task<JsonDocument> OpenApi(SessionHost service)
    {
        using var response = await service.Client().GetAsync("/api/openapi.json", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await Json(response);
    }

    private static async Task<JsonDocument> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    /// <summary>
    /// The platform's own builder, recording what it builds, so a test observes the real
    /// mapping rather than a stand-in for it.
    /// </summary>
    private sealed class SpyContextBuilder : IContextBuilder
    {
        public ServiceContextBuilder? Inner { get; set; }

        public ConcurrentQueue<LdContext> Built { get; } = new();

        public LdContext Build()
        {
            var context = Inner!.Build();
            Built.Enqueue(context);
            return context;
        }
    }
}
