using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// The organization credential: a product service operation that declares
/// <see cref="AcceptsOrganizationCredentialAttribute"/> also accepts a token issued to an
/// organization's API key (an <c>organization</c> claim, no <c>sub</c>), and nothing else does.
/// </summary>
/// <remarks>
/// The Public API acts as the organization's owner with no user, and a service serving it declares
/// this on its operations.
/// </remarks>
public sealed class OrganizationCredentialTests : IAsyncLifetime
{
    private static readonly Guid Organization = new("0c000000-0000-4000-8000-00000000000c");
    private static readonly Guid Other = new("0d000000-0000-4000-8000-00000000000d");
    private static readonly Guid Person = new("a1000000-0000-4000-8000-00000000000a");

    private SessionHost _service = null!;

    public async ValueTask InitializeAsync() => _service = await SessionHost.StartAsync(productService: true);

    public async ValueTask DisposeAsync() => await _service.DisposeAsync();

    [Fact]
    public async Task Credential_ToAnOperationThatAcceptsIt_IsTheOrganizationsOwnerWithNoUser()
    {
        using var response = await Send("/api/v1/probes/credential", Credential(Organization));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var attributes = await Attributes(response);
        Assert.False(attributes.TryGetProperty("actingUser", out _));
        Assert.Equal($"{Organization} Owner True", attributes.GetProperty("note").GetString());
    }

    // Every operation that doesn't declare it refuses a token that names no person, at
    // authentication.
    [Theory]
    [InlineData("/api/v1/probes/current")]
    [InlineData("/api/v1/probes/role")]
    [InlineData("/api/v1/probes/owners")]
    public async Task Credential_ToAnOperationThatDoesNotDeclareIt_Unauthenticated401(string path)
    {
        using var response = await Send(path, Credential(Organization, SessionHost.Scope));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // The scope it must hold is the declared one, and a person's scopes don't stand in for it.
    [Fact]
    public async Task Credential_WithoutTheDeclaredScope_Forbidden()
    {
        using var response = await Send("/api/v1/probes/credential", Credential(Organization, SessionHost.Scope));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // The organization is the credential's, and no header may name one, the same or another.
    [Fact]
    public async Task Credential_WithAnOrganizationHeader_Refused400()
    {
        using var matching = await Send("/api/v1/probes/credential", Credential(Organization), organization: Organization);
        using var other = await Send("/api/v1/probes/credential", Credential(Organization), organization: Other);

        Assert.Equal(HttpStatusCode.BadRequest, matching.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);
    }

    // Recorded as the organization's credential wherever the call goes.
    [Fact]
    public async Task Credential_CallingDownstream_CarriesTheOrganizationCredential()
    {
        using var response = await Send("/api/v1/probes/credential-downstream", Credential(Organization));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(_service.Recorded.Downstream);
        Assert.True(CallerContextHeader.TryParse(sent[BitwardenHeaders.Context], out var carried, out var problem), problem);
        Assert.Equal(CallerCredential.Organization, carried.Credential);
    }

    // An owner meets every role and permission requirement.
    [Fact]
    public async Task Credential_ToAnOwnersOperationNeedingAPermission_Accepted()
    {
        using var response = await Send("/api/v1/probes/credential-owners", Credential(Organization));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // The declaration is that nobody need be behind the request, whatever the operation says
    // about a user for a person's session.
    [Fact]
    public async Task Credential_ToAnOperationRequiringAUser_AcceptedWithNone()
    {
        using var response = await Send("/api/v1/probes/credential-person", Credential(Organization));

        // Nothing in the probe but its identifier: no acting user to report.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("actingUser", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    // Downstream: the organization, never a user, and the service's own credential rather than
    // the customer's token.
    [Fact]
    public async Task Credential_CallingDownstream_CarriesTheOrganizationAndNoUser()
    {
        using var response = await Send("/api/v1/probes/credential-downstream", Credential(Organization));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(_service.Recorded.Downstream);
        Assert.Equal(Organization.ToString(), sent[BitwardenHeaders.Organization]);
        Assert.False(sent.ContainsKey(BitwardenHeaders.User));
        Assert.Equal($"Bearer {SessionHost.ServiceToken}", sent["Authorization"]);
    }

    // A person's session still works on an operation that also accepts the credential, under the
    // operation's own scope.
    [Fact]
    public async Task PersonsSession_ToAnOperationThatAcceptsTheCredential_StillThePerson()
    {
        using var response = await Send("/api/v1/probes/credential", SessionHost.Token(Person, SessionHost.Scope));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Person.ToString(), (await Attributes(response)).GetProperty("actingUser").GetString());
    }

    // A person's organization session carries both sub and organization, and is a person's: it
    // needs the operation's scope, not the credential's.
    [Fact]
    public async Task PersonsOrganizationSession_HoldingOnlyTheCredentialScope_Forbidden()
    {
        var token = SessionHost.Token(
            Person,
            new Dictionary<string, object> { ["organization"] = Organization.ToString(), ["organization_role"] = "Owner" },
            SessionHost.OrganizationCredentialScope);

        using var response = await Send("/api/v1/probes/credential", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // A service's own credential names neither a person nor an organization: still refused.
    [Fact]
    public async Task ServiceToken_ToAnOperationThatAcceptsTheCredential_Unauthenticated401()
    {
        using var response = await Send(
            "/api/v1/probes/credential", SessionHost.Token(user: null, SessionHost.OrganizationCredentialScope));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Below the product layer there is no credential to recognise: the operation's own scope
    // applies and the caller has no role.
    [Fact]
    public async Task Credential_ToADomainService_NotRecognised()
    {
        await using var domain = await SessionHost.StartAsync(productService: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/probes/credential");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Credential(Organization));

        using var response = await domain.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static string Credential(Guid organization, params string[] scopes) =>
        SessionHost.Token(
            user: null,
            new Dictionary<string, object>
            {
                ["organization"] = organization.ToString(),
                ["client_sub"] = $"organization.{organization}",
            },
            scopes.Length > 0 ? scopes : [SessionHost.OrganizationCredentialScope]);

    private async Task<HttpResponseMessage> Send(string path, string token, Guid? organization = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (organization is not null)
        {
            request.Headers.Add(BitwardenHeaders.Organization, organization.Value.ToString());
        }

        return await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> Attributes(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("data").GetProperty("attributes").Clone();
    }
}
