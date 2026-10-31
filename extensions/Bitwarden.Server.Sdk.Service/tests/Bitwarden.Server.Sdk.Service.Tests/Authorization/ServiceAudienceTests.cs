using System.Net;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// A token is accepted only where it is addressed: every service accepts its own name and
/// no other, and none can forget to check.
/// </summary>
/// <remarks>
/// The reason is P06: a person's token leaked or logged at one product service must not work at
/// another, a service's token obtained for one callee must not work at another, and a person's
/// session, which names Identity, must not work anywhere but Identity's exchange.
/// </remarks>
public sealed class ServiceAudienceTests
{
    private static readonly Guid Person = new("a1000000-0000-4000-8000-0000000000a1");

    /// <summary>
    /// Another product service's name, as Identity's exchange would address its token.
    /// </summary>
    private const string AnotherProduct = "another-product-service";

    /// <summary>
    /// What a person's session names: Identity, whose exchange is its only use.
    /// </summary>
    private const string Session = "identity-service";

    // --- The audience follows from the service's name and kind ---------------------------------

    [Fact]
    public async Task Audience_ProductService_IsItsOwnName()
    {
        await using var service = await SessionHost.StartAsync(productService: true);

        Assert.Equal(SessionHost.ServiceName, service.Identity.Audience);
    }

    [Fact]
    public async Task Audience_DomainService_IsItsOwnName()
    {
        await using var service = await SessionHost.StartAsync(productService: false);

        Assert.Equal(SessionHost.ServiceName, service.Identity.Audience);
    }

    // --- A product service ---------------------------------------------------------------------

    [Fact]
    public async Task ProductService_TokenAddressedToIt_Accepted()
    {
        await using var service = await SessionHost.StartAsync(productService: true);

        using var response = await Send(service, [SessionHost.ServiceName], Person);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Person, await ProductServiceAttributeTests.ActingUser(response));
    }

    [Fact]
    public async Task ProductService_TokenAddressedToAnotherProduct_Unauthenticated401()
    {
        await using var service = await SessionHost.StartAsync(productService: true);

        using var response = await Send(service, [AnotherProduct], Person);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // The bare session: its one use is as the subject of Identity's exchange.
    [Fact]
    public async Task ProductService_TheSessionItself_Unauthenticated401()
    {
        await using var service = await SessionHost.StartAsync(productService: true);

        using var response = await Send(service, [Session], Person);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // The platform's audience is a service's; a person's token naming it is no product's.
    [Fact]
    public async Task ProductService_PersonsTokenAddressedToThePlatform_Unauthenticated401()
    {
        await using var service = await SessionHost.StartAsync(productService: true);

        using var response = await Send(service, [TokenAudience.Platform], Person);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProductService_TokenNamingNoAudience_Unauthenticated401()
    {
        await using var service = await SessionHost.StartAsync(productService: true);

        using var response = await Send(service, [], Person);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // RFC 7519: a token naming several audiences is for each of them.
    [Fact]
    public async Task ProductService_TokenNamingItAmongOthers_Accepted()
    {
        await using var service = await SessionHost.StartAsync(productService: true);

        using var response = await Send(service, [AnotherProduct, SessionHost.ServiceName], Person);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- A domain service ----------------------------------------------------------------------

    [Fact]
    public async Task DomainService_ServiceTokenAddressedToIt_Accepted()
    {
        await using var service = await SessionHost.StartAsync(productService: false);

        using var response = await Send(service, [SessionHost.ServiceName], user: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // The platform's audience is retired: a service's token names its callee.
    [Fact]
    public async Task DomainService_ServiceTokenAddressedToThePlatform_Unauthenticated401()
    {
        await using var service = await SessionHost.StartAsync(productService: false);

        using var response = await Send(service, [TokenAudience.Platform], user: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // A token Identity issued another service's caller for another callee.
    [Fact]
    public async Task DomainService_ServiceTokenAddressedToAnotherService_Unauthenticated401()
    {
        await using var service = await SessionHost.StartAsync(productService: false);

        using var response = await Send(service, ["another-domain-service"], user: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // A person's product token never reaches a domain service; if one did, it is refused.
    [Fact]
    public async Task DomainService_TokenAddressedToAProduct_Unauthenticated401()
    {
        await using var service = await SessionHost.StartAsync(productService: false);

        using var response = await Send(service, [AnotherProduct], user: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DomainService_TheSessionItself_Unauthenticated401()
    {
        await using var service = await SessionHost.StartAsync(productService: false);

        using var response = await Send(service, [Session], Person);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- What a service can and can't change ---------------------------------------------------

    // One surface a service hosts may take another audience; the platform keeps the service's own beside it.
    [Fact]
    public async Task DomainService_AnAudienceItAdds_AcceptedBesideItsOwn()
    {
        await using var service = await SessionHost.StartAsync(
            productService: false,
            services: services => services.Configure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options => options.TokenValidationParameters.ValidAudiences = ["hub-audience"]));

        using var added = await Send(service, ["hub-audience"], user: null);
        using var own = await Send(service, [SessionHost.ServiceName], user: null);

        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    // The default can't be configured away by the ordinary means.
    [Fact]
    public async Task ProductService_ConfiguredNotToValidateTheAudience_StillValidates()
    {
        await using var service = await SessionHost.StartAsync(
            productService: true,
            services: services => services.Configure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options => options.TokenValidationParameters.ValidateAudience = false));

        using var response = await Send(service, [AnotherProduct], Person);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> Send(SessionHost service, string[] audiences, Guid? user)
    {
        using var request = ProductServiceAttributeTests.Get(
            "/api/v1/probes/current",
            SessionHost.AddressedToken(audiences, user, new Dictionary<string, object>(), SessionHost.Scope));

        return await service.Client().SendAsync(request, TestContext.Current.CancellationToken);
    }
}
