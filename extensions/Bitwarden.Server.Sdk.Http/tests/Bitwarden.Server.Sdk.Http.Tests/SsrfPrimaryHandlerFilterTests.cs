using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bitwarden.Server.Sdk.Http.Tests;

/// <summary>
/// The filter puts a guarded client's guard below every handler the client adds, and sets its sending handler up to
/// connect only where the guard checked, after every other configuration has run; it leaves other clients, and a client the
/// guard doesn't apply to, as they were.
/// </summary>
public sealed class SsrfPrimaryHandlerFilterTests
{
    [Fact]
    public void ASocketsHandler_IsSetUpToConnectOnlyWhereTheGuardChecked()
    {
        var primary = new SocketsHttpHandler();
        var builder = Builder("guarded", primary);

        Run(new SsrfPrimaryHandlerFilter("guarded", _ => true), builder);

        Assert.False(primary.AllowAutoRedirect);
        Assert.False(primary.UseCookies);
        Assert.IsType<SsrfConnector>(primary.ConnectCallback?.Target);
        Assert.Same(HttpClient.DefaultProxy, primary.Proxy);
        Assert.Same(primary, GuardOf(builder).Sender);
    }

    // A proxy the handler names is the one it keeps.
    [Fact]
    public void AProxyTheHandlerNames_IsKept()
    {
        var proxy = new System.Net.WebProxy("http://proxy.test:3128");
        var primary = new SocketsHttpHandler { Proxy = proxy };

        Run(new SsrfPrimaryHandlerFilter("guarded", _ => true), Builder("guarded", primary));

        Assert.Same(proxy, primary.Proxy);
    }

    // The guard goes below every handler the client added, whenever it was added, so they run once for each request
    // and never for a redirect the guard follows.
    [Fact]
    public void TheGuard_GoesBelowEveryOtherHandler()
    {
        var builder = Builder("guarded", new SocketsHttpHandler());
        var guard = GuardOf(builder);
        var later = new HandingOn();
        builder.AdditionalHandlers.Add(later);

        Run(new SsrfPrimaryHandlerFilter("guarded", _ => true), builder);

        Assert.Equal([later, guard], builder.AdditionalHandlers);
    }

    // The configuration before it runs first, so a primary handler it sets is the one set up.
    [Fact]
    public void APrimaryHandlerSetByAnEarlierConfiguration_IsSetUp()
    {
        var primary = new SocketsHttpHandler();
        var builder = Builder("guarded", new SocketsHttpHandler());

        new SsrfPrimaryHandlerFilter("guarded", _ => true).Configure(next => next.PrimaryHandler = primary)(builder);

        Assert.False(primary.AllowAutoRedirect);
        Assert.Same(primary, GuardOf(builder).Sender);
    }

    // A handler wrapping the one that sends would sit below the guard and see every redirect hop.
    [Fact]
    public void AWrappedSocketsHandler_Throws()
    {
        var builder = Builder("guarded", new HandingOn(new SocketsHttpHandler()));

        Assert.Throws<InvalidOperationException>(() => Run(new SsrfPrimaryHandlerFilter("guarded", _ => true), builder));
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
        var builder = Builder("guarded", primary, guarded: false);
        IServiceProvider? asked = null;

        Run(new SsrfPrimaryHandlerFilter("guarded", services =>
        {
            asked = services;
            return false;
        }), builder);

        Assert.True(primary.AllowAutoRedirect);
        Assert.True(primary.UseCookies);
        Assert.Null(primary.ConnectCallback);
        Assert.Same(builder.Services, asked);
    }

    // A client whose handlers were cleared after the guard was added (a test answering in process) is left as it is.
    [Fact]
    public void AClientWithoutItsGuard_IsLeftAlone()
    {
        var primary = new SocketsHttpHandler();

        Run(new SsrfPrimaryHandlerFilter("guarded", _ => true), Builder("guarded", primary, guarded: false));

        Assert.True(primary.AllowAutoRedirect);
        Assert.Null(primary.ConnectCallback);
    }

    // A client guarded twice is set up twice, the second time as the first left it.
    [Fact]
    public void TwoFilters_SetTheClientUpOnce()
    {
        var primary = new SocketsHttpHandler();
        var builder = Builder("guarded", primary);

        Run(new SsrfPrimaryHandlerFilter(null, _ => true), builder);
        Run(new SsrfPrimaryHandlerFilter("guarded", _ => true), builder);

        Assert.Same(primary, GuardOf(builder).Sender);
        Assert.Single(builder.AdditionalHandlers);
    }

    // An HttpClientHandler has no ConnectCallback to pin connections with: the client fails to be created, naming
    // itself, rather than going unguarded.
    [Fact]
    public void AnHttpClientHandler_Throws()
    {
        using var primary = new HttpClientHandler();

        var exception = Assert.Throws<InvalidOperationException>(
            () => Run(new SsrfPrimaryHandlerFilter("guarded", _ => true), Builder("guarded", primary)));

        Assert.Contains("'guarded'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(HttpClientHandler), exception.Message, StringComparison.Ordinal);
    }

    // A test's fake has no ConnectCallback either.
    [Fact]
    public void AHandlerThatIsNotASocketsHandler_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => Run(new SsrfPrimaryHandlerFilter("guarded", _ => true), Builder("guarded", new Answering())));
    }

    // A ConnectCallback of the client's own would decide where connections go instead of the guard.
    [Fact]
    public void ASocketsHandlerWithItsOwnConnectCallback_Throws()
    {
        var primary = new SocketsHttpHandler { ConnectCallback = (_, _) => throw new InvalidOperationException("Never connects.") };

        Assert.Throws<InvalidOperationException>(
            () => Run(new SsrfPrimaryHandlerFilter("guarded", _ => true), Builder("guarded", primary)));
    }

    private static void Run(SsrfPrimaryHandlerFilter filter, HttpMessageHandlerBuilder builder) =>
        filter.Configure(_ => { })(builder);

    private static SsrfProtectionHandler GuardOf(HttpMessageHandlerBuilder builder) =>
        builder.AdditionalHandlers.OfType<SsrfProtectionHandler>().Single();

    private static FakeBuilder Builder(string name, HttpMessageHandler primary, bool guarded = true)
    {
        var builder = new FakeBuilder { Name = name, PrimaryHandler = primary };
        if (guarded)
        {
            builder.AdditionalHandlers.Add(
                new SsrfProtectionHandler(NullLogger<SsrfProtectionHandler>.Instance, builder.Services.GetRequiredService<SsrfConnector>()));
        }

        return builder;
    }

    /// <summary>
    /// A handler builder holding what a configuration sets, with the network the guard uses as its only service.
    /// </summary>
    private sealed class FakeBuilder : HttpMessageHandlerBuilder
    {
        private readonly ServiceProvider _services = new ServiceCollection().AddSingleton<SsrfConnector>().BuildServiceProvider();

        public override string? Name { get; set; }

        public override HttpMessageHandler PrimaryHandler { get; set; } = null!;

        public override IList<DelegatingHandler> AdditionalHandlers { get; } = [];

        public override IServiceProvider Services => _services;

        public override HttpMessageHandler Build() => CreateHandlerPipeline(PrimaryHandler, AdditionalHandlers);
    }

    private sealed class HandingOn : DelegatingHandler
    {
        public HandingOn()
        {
        }

        public HandingOn(HttpMessageHandler inner)
            : base(inner)
        {
        }
    }

    private sealed class Answering : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
