using Bitwarden.Server.Sdk.Environment;
using Bitwarden.Server.Sdk.Errors;
using Bitwarden.Server.Sdk.Resources.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Resources.Tests.Endpoints;

/// <summary>
/// The filter refuses an operation the install does not have, keeps a status the handler chose,
/// and gives a create, when the handler names none, a <c>Location</c> that is the request's own
/// address and the new resource's identifier.
/// </summary>
public sealed class StandardEndpointFilterTests
{
    [Fact]
    public void Succeeded_Created_LocationIsTheCollectionAndId()
    {
        var http = Created(pathBase: string.Empty);

        Assert.Equal("/api/v1/widgets/abc", http.Response.Headers.Location.ToString());
    }

    // A service mapped below a path base is reached through it, so the address must name it.
    [Fact]
    public void Succeeded_CreatedBelowAPathBase_LocationIncludesIt()
    {
        var http = Created(pathBase: "/widget-service");

        Assert.Equal("/widget-service/api/v1/widgets/abc", http.Response.Headers.Location.ToString());
    }

    [Fact]
    public void Succeeded_HandlerChoseItsStatus_Kept()
    {
        var http = new DefaultHttpContext();
        http.Response.StatusCode = StatusCodes.Status202Accepted;

        StandardEndpointFilter.Succeeded(
            http, new ResourceDocument<Widget> { Data = new Widget { Id = "abc" } }, StatusCodes.Status201Created);

        Assert.Equal(StatusCodes.Status202Accepted, http.Response.StatusCode);
        Assert.False(http.Response.Headers.ContainsKey("Location"));
    }

    [Theory]
    [InlineData(nameof(Handlers.SelfHostedOnly))]
    [InlineData(nameof(SelfHostedHandlers.SelfHostedByItsClass))]
    public async Task Create_SelfHostedOnlyOffSelfHosted_Refused(string handler)
    {
        var refused = await Assert.ThrowsAsync<InvalidRequestException>(() => Invoke(handler, selfHosted: false));

        Assert.Equal("Only allowed when self hosted.", refused.Message);
    }

    [Theory]
    [InlineData(nameof(Handlers.NotSelfHostedOnly))]
    [InlineData(nameof(CloudHandlers.NotSelfHostedByItsClass))]
    public async Task Create_NotSelfHostedOnlyOnSelfHosted_Refused(string handler)
    {
        var refused = await Assert.ThrowsAsync<InvalidRequestException>(() => Invoke(handler, selfHosted: true));

        Assert.Equal("Only allowed when not self hosted.", refused.Message);
    }

    [Theory]
    [InlineData(nameof(Handlers.SelfHostedOnly), true)]
    [InlineData(nameof(Handlers.NotSelfHostedOnly), false)]
    [InlineData(nameof(Handlers.Anywhere), true)]
    [InlineData(nameof(Handlers.Anywhere), false)]
    public async Task Create_OperationTheInstallHas_HandlerRuns(string handler, bool selfHosted)
    {
        Assert.True(await Invoke(handler, selfHosted));
    }

    /// <returns>Whether the handler ran.</returns>
    private static async Task<bool> Invoke(string handler, bool selfHosted)
    {
        var method = typeof(Handlers).GetMethod(handler)
            ?? typeof(SelfHostedHandlers).GetMethod(handler)
            ?? typeof(CloudHandlers).GetMethod(handler)!;
        var ran = false;
        var filter = StandardEndpointFilter.Create(
            new EndpointFilterFactoryContext { MethodInfo = method, ApplicationServices = new ServiceCollection().BuildServiceProvider() },
            _ =>
            {
                ran = true;
                return ValueTask.FromResult<object?>(null);
            });
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton<IBitwardenEnvironment>(new StubEnvironment(selfHosted))
                .BuildServiceProvider(),
        };

        await filter(new DefaultEndpointFilterInvocationContext(http));

        return ran;
    }

    private static DefaultHttpContext Created(string pathBase)
    {
        var http = new DefaultHttpContext();
        http.Request.PathBase = pathBase;
        http.Request.Path = "/api/v1/widgets/";

        StandardEndpointFilter.Succeeded(
            http, new ResourceDocument<Widget> { Data = new Widget { Id = "abc" } }, StatusCodes.Status201Created);

        return http;
    }

    public sealed record Widget : Resource;

    private sealed class StubEnvironment(bool selfHosted) : IBitwardenEnvironment
    {
        public string Version => "1.0.0";

        public string? GitHash => null;

        public bool SelfHosted => selfHosted;

        public string? SelfHostFlavor => null;
    }

    private static class Handlers
    {
        [SelfHostedOnly]
        public static void SelfHostedOnly()
        {
        }

        [NotSelfHostedOnly]
        public static void NotSelfHostedOnly()
        {
        }

        public static void Anywhere()
        {
        }
    }

    [SelfHostedOnly]
    private static class SelfHostedHandlers
    {
        public static void SelfHostedByItsClass()
        {
        }
    }

    [NotSelfHostedOnly]
    private static class CloudHandlers
    {
        public static void NotSelfHostedByItsClass()
        {
        }
    }
}
