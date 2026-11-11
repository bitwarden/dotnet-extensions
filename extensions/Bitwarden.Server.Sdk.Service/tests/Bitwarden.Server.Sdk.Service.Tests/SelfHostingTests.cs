using System.Net;
using System.Text.Json;
using Bitwarden.Server.Sdk.Environment;
using Bitwarden.Server.Sdk.Resources;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace Bitwarden.Server.Sdk.Service.Tests;

/// <summary>
/// A service knows whether it is part of a self-hosted install from <c>globalSettings:selfHosted</c>:
/// <see cref="IBitwardenEnvironment.SelfHosted"/>, <c>appsettings.SelfHosted.json</c>, and the
/// self-hosted-only and cloud-only refusals.
/// </summary>
public sealed class SelfHostingTests
{
    private static readonly Guid Person = new("a1000000-0000-4000-8000-00000000000e");

    private static readonly Dictionary<string, string?> SelfHosted = new() { ["globalSettings:selfHosted"] = "true" };

    [Fact]
    public async Task Unset_IsCloud()
    {
        await using var service = await SessionHost.StartAsync(productService: false);

        Assert.False(service.Resolve<IBitwardenEnvironment>().SelfHosted);
        Assert.DoesNotContain(JsonSources(service), path => path == "appsettings.SelfHosted.json");
    }

    [Fact]
    public async Task SelfHosted_IsSelfHosted_WithItsSettingsAfterTheEnvironmentsAndBeforeTheDeployments()
    {
        await using var service = await SessionHost.StartAsync(productService: false, SelfHosted);

        Assert.True(service.Resolve<IBitwardenEnvironment>().SelfHosted);
        var sources = JsonSources(service);
        Assert.Equal(sources.IndexOf("appsettings.Testing.json") + 1, sources.IndexOf("appsettings.SelfHosted.json"));
    }

    // An install that names no flavor is still self-hosted, of a flavor nobody knows.
    [Fact]
    public async Task SelfHosted_WithoutAFlavor_IsOfAnUnknownFlavor()
    {
        await using var service = await SessionHost.StartAsync(productService: false, SelfHosted);

        Assert.Equal("unknown", service.Resolve<IBitwardenEnvironment>().SelfHostFlavor);
    }

    [Fact]
    public async Task SelfHosted_WithAFlavor_IsOfThatFlavor()
    {
        await using var service = await SessionHost.StartAsync(
            productService: false,
            new Dictionary<string, string?>(SelfHosted) { ["globalSettings:selfHostFlavor"] = "docker" });

        Assert.Equal("docker", service.Resolve<IBitwardenEnvironment>().SelfHostFlavor);
    }

    [Theory]
    [InlineData(false, "self-hosted-only", "Only allowed when self hosted.")]
    [InlineData(true, "cloud-only", "Only allowed when not self hosted.")]
    public async Task Refused_WithTheStandardSentence(bool selfHosted, string probe, string detail)
    {
        await using var service = await SessionHost.StartAsync(productService: false, selfHosted ? SelfHosted : null);

        using var response = await Get(service, probe);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(detail, JsonDocument.Parse(body).RootElement.GetRawText());
    }

    [Theory]
    [InlineData(true, "self-hosted-only")]
    [InlineData(false, "cloud-only")]
    public async Task Allowed_WhereTheInstallHasIt(bool selfHosted, string probe)
    {
        await using var service = await SessionHost.StartAsync(productService: false, selfHosted ? SelfHosted : null);

        using var response = await Get(service, probe);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> Get(SessionHost service, string probe)
    {
        using var request = ProductServiceAttributeTests.Get($"/api/v1/install-probes/{probe}", SessionHost.Token(Person, SessionHost.Scope));
        return await service.Client().SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static List<string> JsonSources(SessionHost service) =>
        ((IConfigurationRoot)service.Resolve<IConfiguration>()).Providers
            .OfType<JsonConfigurationProvider>()
            .Select(provider => provider.Source.Path ?? "")
            .ToList();
}

/// <summary>
/// A probe of what kind of install answers, for <see cref="SelfHostingTests"/>.
/// </summary>
public sealed record InstallProbe : Resource;
