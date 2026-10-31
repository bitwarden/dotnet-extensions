using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// [NamesOrganization] and [NamesProvider]: an operation about an organization or provider the
/// person can't hold a session for yet names it in its path or body. It never enters
/// the service's own context, grants nothing by role, and is carried on every downstream call.
/// </summary>
public sealed class RequestNamedOwnerTests : IAsyncLifetime
{
    private static readonly Guid Person = new("a1000000-0000-4000-8000-00000000000a");
    private static readonly Guid OrganizationA = new("a0000000-0000-4000-8000-0000000000aa");
    private static readonly Guid Provider = new("c0000000-0000-4000-8000-0000000000cc");

    private SessionHost _service = null!;

    public async ValueTask InitializeAsync() => _service = await SessionHost.StartAsync(productService: true);

    public async ValueTask DisposeAsync() => await _service.DisposeAsync();

    [Fact]
    public async Task Path_PersonsSessionForNoOrganization_NoRoleAndNothingInContext()
    {
        using var response = await _service.Client().SendAsync(
            ProductServiceAttributeTests.Get($"/api/v1/organizations/{OrganizationA}/probe", SessionHost.Token(Person, SessionHost.Scope)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(" None False", await Note(response));
    }

    // Every call the operation makes carries it, with the person.
    [Fact]
    public async Task Path_Downstream_EveryCallCarriesTheOrganizationAndThePerson()
    {
        using var response = await _service.Client().SendAsync(
            ProductServiceAttributeTests.Get($"/api/v1/organizations/{OrganizationA}/probe", SessionHost.Token(Person, SessionHost.Scope)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = _service.Recorded.Downstream.TakeLast(2).ToList();
        Assert.Equal(2, sent.Count);
        Assert.All(sent, call =>
        {
            Assert.Equal(OrganizationA.ToString(), call[BitwardenHeaders.Organization]);
            Assert.Equal(Person.ToString(), call[BitwardenHeaders.User]);
        });
    }

    // A session for an organization names that one, and only that one: it can't name another here.
    [Fact]
    public async Task Path_ASessionForAnOrganization_Forbidden()
    {
        using var response = await _service.Client().SendAsync(
            ProductServiceAttributeTests.Get($"/api/v1/organizations/{OrganizationA}/probe", Session(Guid.NewGuid(), "Owner")),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_service.Recorded.Downstream);
    }

    [Fact]
    public async Task Body_Downstream_CarriesTheOrganizationAndThePerson()
    {
        using var response = await Post("/api/v1/probes/current/actions/named", new { organizationId = OrganizationA });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var sent = _service.Recorded.Downstream.Last();
        Assert.Equal(OrganizationA.ToString(), sent[BitwardenHeaders.Organization]);
        Assert.Equal(Person.ToString(), sent[BitwardenHeaders.User]);
    }

    [Fact]
    public async Task Body_Provider_CarriesTheProviderAndThePerson()
    {
        using var response = await Post("/api/v1/probes/current/actions/named-provider", new { providerId = Provider });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var sent = _service.Recorded.Downstream.Last();
        Assert.Equal(Provider.ToString(), sent[BitwardenHeaders.Provider]);
        Assert.Equal(Person.ToString(), sent[BitwardenHeaders.User]);
        Assert.False(sent.ContainsKey(BitwardenHeaders.Organization));
    }

    // Any other operation's calls carry what the session names, and nothing a body says.
    [Fact]
    public async Task Undeclared_Downstream_CarriesNoOrganization()
    {
        using var response = await _service.Client().SendAsync(
            ProductServiceAttributeTests.Get("/api/v1/probes/downstream", SessionHost.Token(Person, SessionHost.Scope)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(_service.Recorded.Downstream.Last().ContainsKey(BitwardenHeaders.Organization));
    }

    [Fact]
    public async Task Path_NoToken_Unauthorized()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/organizations/{OrganizationA}/probe");

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpResponseMessage> Post(string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SessionHost.Token(Person, SessionHost.WriteScope));

        return await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string Session(Guid organization, string role) =>
        SessionHost.Token(
            Person,
            new Dictionary<string, object> { ["organization_role"] = role, ["organization"] = organization.ToString() },
            SessionHost.Scope);

    private static async Task<string?> Note(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("note").GetString();
    }
}
