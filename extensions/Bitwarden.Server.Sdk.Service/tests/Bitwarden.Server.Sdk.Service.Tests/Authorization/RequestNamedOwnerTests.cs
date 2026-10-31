using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// [NamesOrganization] and [NamesProvider]: an operation about an organization or provider the
/// person can't hold a session for yet names it in its path or body. It never enters
/// the service's own context and grants nothing by role.
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

    // A session for an organization names that one, and only that one: it can't name another here.
    [Fact]
    public async Task Path_ASessionForAnOrganization_Forbidden()
    {
        using var response = await _service.Client().SendAsync(
            ProductServiceAttributeTests.Get($"/api/v1/organizations/{OrganizationA}/probe", Session(Guid.NewGuid(), "Owner")),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Body_Organization_Accepted()
    {
        using var response = await Post("/api/v1/probes/current/actions/named", new { organizationId = OrganizationA });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Body_Provider_Accepted()
    {
        using var response = await Post("/api/v1/probes/current/actions/named-provider", new { providerId = Provider });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
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
