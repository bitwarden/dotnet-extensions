using System.Net;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// [CreatesOrganization]: an operation creating an organization may name it on its own downstream
/// calls, once, and nothing else in a product service may.
/// </summary>
public sealed class CreatesOrganizationAttributeTests : IAsyncLifetime
{
    private static readonly Guid Person = new("a1000000-0000-4000-8000-00000000000a");

    private SessionHost _service = null!;

    public async ValueTask InitializeAsync() => _service = await SessionHost.StartAsync(productService: true);

    public async ValueTask DisposeAsync() => await _service.DisposeAsync();

    private async Task<HttpResponseMessage> Get(string probe) =>
        await _service.Client().SendAsync(
            ProductServiceAttributeTests.Get($"/api/v1/probes/{probe}", SessionHost.Token(Person, SessionHost.Scope)),
            TestContext.Current.CancellationToken);

    /// <summary>
    /// Whether the request was refused: an error answered, or the failure thrown through the test host.
    /// </summary>
    private async Task<bool> Refused(string probe)
    {
        try
        {
            using var response = await Get(probe);
            return response.StatusCode != HttpStatusCode.OK;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    [Fact]
    public async Task Creates_NamesTheCreatedOrganizationAndThePerson()
    {
        using var response = await Get("creates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = _service.Recorded.Downstream.Last();
        Assert.Equal(Probes.CreatedOrganization.ToString(), sent[BitwardenHeaders.Organization]);
        Assert.Equal(Person.ToString(), sent[BitwardenHeaders.User]);
    }

    // One organization per request: the first named is the only one a later call may name.
    [Fact]
    public async Task Creates_ASecondOrganizationInTheSameRequest_Refused()
    {
        var before = _service.Recorded.Downstream.Count;

        Assert.True(await Refused("creates-two"));

        Assert.Equal(before + 1, _service.Recorded.Downstream.Count);
    }

    [Fact]
    public async Task Undeclared_NamingACreatedOrganization_Refused()
    {
        var before = _service.Recorded.Downstream.Count;

        Assert.True(await Refused("creates-undeclared"));

        Assert.Equal(before, _service.Recorded.Downstream.Count);
    }
}
