using System.Net;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;

namespace Bitwarden.Server.Sdk.Service.Tests;

/// <summary>
/// A service that does not opt in behaves exactly as a domain service always has.
/// </summary>
public sealed class DomainServiceSessionTests : IAsyncLifetime
{
    private static readonly Guid Named = new("c3000000-0000-4000-8000-00000000000c");

    private SessionHost _service = null!;

    public async ValueTask InitializeAsync() => _service = await SessionHost.StartAsync(productService: false);

    public async ValueTask DisposeAsync() => await _service.DisposeAsync();

    [Fact]
    public void Identity_DomainService_IsNotAProductService()
    {
        Assert.False(_service.Identity.IsProductService);
    }

    // The caller is another service, which says whom it acts for.
    [Fact]
    public async Task GetProbe_ServiceTokenNamingAUserInTheHeader_ActsAsThatUser()
    {
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/current", SessionHost.Token(user: null, SessionHost.Scope));
        request.Headers.Add(BitwardenHeaders.User, Named.ToString());

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Named, await ProductServiceAttributeTests.ActingUser(response));
    }

    // A token's sub means nothing here: the acting user is only ever the header.
    [Fact]
    public async Task GetProbe_TokenWithSubAndNoHeader_ActsForNobody()
    {
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/current", SessionHost.Token(Named, SessionHost.Scope));

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await ProductServiceAttributeTests.ActingUser(response));
    }

    [Fact]
    public async Task GetDownstreamProbe_HeaderNamingAUser_CarriesTheHeadersUser()
    {
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/downstream", SessionHost.Token(user: null, SessionHost.Scope));
        request.Headers.Add(BitwardenHeaders.User, Named.ToString());

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Named.ToString(), Assert.Single(_service.Recorded.Downstream)[BitwardenHeaders.User]);
    }

    // A person's token is not a service's: a service a browser can reach would otherwise believe a
    // forged header. A domain service believes the headers from another service alone.
    [Fact]
    public async Task GetProbe_PersonsTokenNamingAUserInTheHeader_ActsForNobody()
    {
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/current", SessionHost.Token(Named, SessionHost.Scope));
        request.Headers.Add(BitwardenHeaders.User, Guid.NewGuid().ToString());

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await ProductServiceAttributeTests.ActingUser(response));
    }

    // Nothing from a caller who isn't a service is carried onward either.
    [Fact]
    public async Task GetDownstreamProbe_PersonsTokenWithHeaders_CarriesNone()
    {
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/downstream", SessionHost.Token(Named, SessionHost.Scope));
        request.Headers.Add(BitwardenHeaders.User, Guid.NewGuid().ToString());
        request.Headers.Add(BitwardenHeaders.Context, """{"v":1,"clientIp":"198.51.100.66","credential":"USER"}""");

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(_service.Recorded.Downstream);
        Assert.False(sent.ContainsKey(BitwardenHeaders.User));
        Assert.Equal("""{"v":1}""", sent[BitwardenHeaders.Context]);
    }

    // From a service, the context travels on unchanged.
    [Fact]
    public async Task GetDownstreamProbe_ContextFromAService_CarriesItOn()
    {
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/downstream", SessionHost.Token(user: null, SessionHost.Scope));
        request.Headers.Add(BitwardenHeaders.User, Named.ToString());
        request.Headers.Add(BitwardenHeaders.Context, """{"v":1,"clientIp":"203.0.113.9","deviceType":"10","credential":"USER"}""");

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            """{"v":1,"clientIp":"203.0.113.9","deviceType":"10","credential":"USER"}""",
            Assert.Single(_service.Recorded.Downstream)[BitwardenHeaders.Context]);
    }

    // Parsed strictly, and refused before anything reads it.
    [Theory]
    [InlineData("""{"v":1,"organization":"a0000000-0000-4000-8000-000000000001"}""")]
    [InlineData("""{"v":1,"clientIp":"198.51.100.1","clientIp":"198.51.100.2"}""")]
    [InlineData("""{"v":2}""")]
    [InlineData("not json")]
    public async Task GetDownstreamProbe_AMalformedContextFromAService_Refused400(string header)
    {
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/downstream", SessionHost.Token(user: null, SessionHost.Scope));
        request.Headers.TryAddWithoutValidation(BitwardenHeaders.Context, header);

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_service.Recorded.Downstream);
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var error = document.RootElement.GetProperty("errors")[0];
        Assert.Equal(BitwardenHeaders.Context, error.GetProperty("source").GetProperty("header").GetString());
    }

    [Fact]
    public async Task GetProbe_ContextSentTwice_Refused400()
    {
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/current", SessionHost.Token(user: null, SessionHost.Scope));
        request.Headers.TryAddWithoutValidation(BitwardenHeaders.Context, ["""{"v":1}""", """{"v":1}"""]);

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // From anybody else it is ignored with the rest, not refused: it isn't theirs to send.
    [Fact]
    public async Task GetProbe_AMalformedContextFromAPerson_Ignored()
    {
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/current", SessionHost.Token(Named, SessionHost.Scope));
        request.Headers.TryAddWithoutValidation(BitwardenHeaders.Context, "not json");

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetProbe_ContextAtTheLimit_Accepted()
    {
        var padding = new string(' ', CallerContextHeader.MaximumLength - """{"v":1}""".Length);
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/current", SessionHost.Token(user: null, SessionHost.Scope));
        request.Headers.TryAddWithoutValidation(BitwardenHeaders.Context, "{\"v\":1" + padding + "}");

        using var response = await _service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_DomainService_OffersTheFourHeaders()
    {
        using var document = await ProductServiceAttributeTests.OpenApi(_service);

        var schemes = document.RootElement.GetProperty("components").GetProperty("securitySchemes");
        Assert.Equal(BitwardenHeaders.Organization, schemes.GetProperty("organization").GetProperty("name").GetString());
        Assert.Equal(BitwardenHeaders.User, schemes.GetProperty("user").GetProperty("name").GetString());
        Assert.Equal(BitwardenHeaders.Provider, schemes.GetProperty("provider").GetProperty("name").GetString());
        Assert.Equal(BitwardenHeaders.Context, schemes.GetProperty("context").GetProperty("name").GetString());
    }
}
