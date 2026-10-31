using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// [AnyScope]: an operation any one of several scopes admits, which then decides what each may ask.
/// </summary>
public sealed class AnyScopeAttributeTests
{
    private static async Task<(HttpStatusCode Status, string? Note)> Get(SessionHost service, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/probes/any-scope");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await service.Client().SendAsync(request, TestContext.Current.CancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return (response.StatusCode, null);
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (response.StatusCode, document.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("note").GetString());
    }

    [Theory]
    [InlineData(SessionHost.Scope, "True False")]
    [InlineData(SessionHost.OtherScope, "False True")]
    public async Task Get_EitherScope_Admitted(string scope, string expected)
    {
        await using var service = await SessionHost.StartAsync(productService: false);

        var (status, note) = await Get(service, SessionHost.Token(null, scope));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(expected, note);
    }

    [Fact]
    public async Task Get_NeitherScope_Forbidden()
    {
        await using var service = await SessionHost.StartAsync(productService: false);

        var (status, _) = await Get(service, SessionHost.Token(null, SessionHost.WriteScope));

        Assert.Equal(HttpStatusCode.Forbidden, status);
    }

    [Fact]
    public async Task Get_NoToken_Unauthorized()
    {
        await using var service = await SessionHost.StartAsync(productService: false);

        var (status, _) = await Get(service, null);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    // Its provider left forbidden, as by default: a call naming one is refused, whichever scope it holds.
    [Fact]
    public async Task Get_NamingAProviderWhereItIsForbidden_Forbidden()
    {
        await using var service = await SessionHost.StartAsync(productService: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/probes/any-scope");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SessionHost.Token(null, SessionHost.Scope));
        request.Headers.Add(BitwardenHeaders.Provider, Guid.NewGuid().ToString());

        using var response = await service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
