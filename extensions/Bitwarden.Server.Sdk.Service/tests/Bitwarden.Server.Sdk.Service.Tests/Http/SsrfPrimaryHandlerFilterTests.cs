using Bitwarden.Server.Sdk.Service.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.Http;

/// <summary>
/// The filter turns automatic redirects off on a guarded client's sending handler after every other configuration
/// has run, and leaves other clients, and a client the guard doesn't apply to, as they were.
/// </summary>
public sealed class SsrfPrimaryHandlerFilterTests
{
    [Fact]
    public void ASocketsHandler_StopsFollowingRedirects()
    {
        var primary = new SocketsHttpHandler();

        Run(new SsrfPrimaryHandlerFilter("guarded", _ => true), Builder("guarded", primary));

        Assert.False(primary.AllowAutoRedirect);
    }

    [Fact]
    public void AnHttpClientHandler_StopsFollowingRedirects()
    {
        using var primary = new HttpClientHandler();

        Run(new SsrfPrimaryHandlerFilter("guarded", _ => true), Builder("guarded", primary));

        Assert.False(primary.AllowAutoRedirect);
    }

    // The configuration before it runs first, so a primary handler it sets is the one turned off.
    [Fact]
    public void APrimaryHandlerSetByAnEarlierConfiguration_StopsFollowingRedirects()
    {
        var primary = new SocketsHttpHandler();
        var builder = Builder("guarded", new SocketsHttpHandler());

        new SsrfPrimaryHandlerFilter("guarded", _ => true).Configure(next => next.PrimaryHandler = primary)(builder);

        Assert.False(primary.AllowAutoRedirect);
    }

    // A primary handler handing requests on is followed, through every layer, to the one that sends them.
    [Fact]
    public void HandlersHandingRequestsOn_AreFollowedToTheSender()
    {
        var sender = new SocketsHttpHandler();
        var primary = new HandingOn(new HandingOn(sender));

        Run(new SsrfPrimaryHandlerFilter(null, _ => true), Builder("any", primary));

        Assert.False(sender.AllowAutoRedirect);
    }

    // With no name every client is guarded; with one, only that client.
    [Theory]
    [InlineData(null, "other", false)]
    [InlineData("guarded", "guarded", false)]
    [InlineData("guarded", "other", true)]
    public void OnlyTheNamedClient_IsGuarded(string? guarded, string client, bool followsRedirects)
    {
        var primary = new SocketsHttpHandler();

        Run(new SsrfPrimaryHandlerFilter(guarded, _ => true), Builder(client, primary));

        Assert.Equal(followsRedirects, primary.AllowAutoRedirect);
    }

    // Where the guard doesn't apply (a self-hosted install, read from the client's services), nothing changes.
    [Fact]
    public void WhereTheGuardDoesNotApply_TheHandlerIsLeftAlone()
    {
        var primary = new SocketsHttpHandler();
        var builder = Builder("guarded", primary);
        IServiceProvider? asked = null;

        Run(new SsrfPrimaryHandlerFilter("guarded", services =>
        {
            asked = services;
            return false;
        }), builder);

        Assert.True(primary.AllowAutoRedirect);
        Assert.Same(builder.Services, asked);
    }

    // A handler with no redirect switch, a fake standing in for the network, is left as it is.
    [Fact]
    public void AHandlerWithoutARedirectSwitch_IsLeftAsItIs()
    {
        var primary = new Answering();
        var builder = Builder("guarded", primary);

        Run(new SsrfPrimaryHandlerFilter("guarded", _ => true), builder);

        Assert.Same(primary, builder.PrimaryHandler);
    }

    private static void Run(SsrfPrimaryHandlerFilter filter, HttpMessageHandlerBuilder builder) =>
        filter.Configure(_ => { })(builder);

    private static FakeBuilder Builder(string name, HttpMessageHandler primary) =>
        new() { Name = name, PrimaryHandler = primary };

    /// <summary>
    /// A handler builder holding what a configuration sets, with no services of its own.
    /// </summary>
    private sealed class FakeBuilder : HttpMessageHandlerBuilder
    {
        private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();

        public override string? Name { get; set; }

        public override HttpMessageHandler PrimaryHandler { get; set; } = null!;

        public override IList<DelegatingHandler> AdditionalHandlers { get; } = [];

        public override IServiceProvider Services => _services;

        public override HttpMessageHandler Build() => CreateHandlerPipeline(PrimaryHandler, AdditionalHandlers);
    }

    private sealed class HandingOn(HttpMessageHandler inner) : DelegatingHandler(inner);

    private sealed class Answering : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
