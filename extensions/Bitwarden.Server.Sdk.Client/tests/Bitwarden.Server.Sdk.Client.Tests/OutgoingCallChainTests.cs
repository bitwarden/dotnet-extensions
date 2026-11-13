using Bitwarden.Server.Sdk.Context;
using Microsoft.Extensions.DependencyInjection;
using static Bitwarden.Server.Sdk.Client.Tests.ContextHandlerHarness;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// The call chain a call carries: the services its request passed through, this one last.
/// </summary>
public sealed class OutgoingCallChainTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // The services the request passed through, this one added last, so a service called back while its own request
    // waits on this call can tell.
    [Fact]
    public async Task Send_InARequestWithAChain_CarriesItWithThisServiceLast()
    {
        HttpRequestMessage sent;

        using (CallChain.Enter(["edge-service", "account-service"]))
        {
            sent = await Send(new StatedContext(), new Identity(isProductService: false));
        }

        Assert.Equal(
            "edge-service,account-service,some-service",
            sent.Headers.GetValues(BitwardenHeaders.CallChain).Single());
    }

    // A request from no service (a person, a job, a message) starts a chain with this service.
    [Fact]
    public async Task Send_InARequestWithNoChain_StartsOneWithThisService()
    {
        var sent = await Send(new StatedContext(), new Identity(isProductService: true));

        Assert.Equal("some-service", sent.Headers.GetValues(BitwardenHeaders.CallChain).Single());
    }

    // The header is this handler's: whatever a call site put there goes, and a process that isn't a service sends none.
    [Fact]
    public async Task Send_WithAChainTheCallSiteSet_ReplacesIt()
    {
        var recorder = new Recorder();
        var services = new ServiceCollection()
            .AddSingleton<IContext>(new StatedContext())
            .AddSingleton<IServiceIdentity>(new Identity(isProductService: false))
            .BuildServiceProvider();
        using var invoker = new HttpMessageInvoker(
            new ContextHandler(services.GetRequiredService<IServiceScopeFactory>(), new Signer()) { InnerHandler = recorder });
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://widget-service.invalid/api/v1/widgets");
        request.Headers.Add(BitwardenHeaders.CallChain, "made-up-service");

        using var response = await invoker.SendAsync(request, Token);

        Assert.Equal("some-service", recorder.Sent!.Headers.GetValues(BitwardenHeaders.CallChain).Single());
    }

    [Fact]
    public async Task Send_NotFromAService_SendsNoChain()
    {
        HttpRequestMessage sent;

        using (CallChain.Enter(["account-service"]))
        {
            sent = await Send(new StatedContext());
        }

        Assert.False(sent.Headers.Contains(BitwardenHeaders.CallChain));
    }
}
