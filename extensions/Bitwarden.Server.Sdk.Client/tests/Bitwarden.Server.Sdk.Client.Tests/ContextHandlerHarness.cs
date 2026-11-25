using System.Net;
using Bitwarden.Server.Sdk.Context;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// Sends a request through <see cref="ContextHandler"/>, with the stand-ins the context tests share.
/// </summary>
internal static class ContextHandlerHarness
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    internal static ContextValue Carried(HttpRequestMessage sent)
    {
        Assert.True(
            ContextHeader.TryParse(sent.Headers.GetValues(BitwardenHeaders.Context).Single(), out var carried, out var problem),
            problem);
        return carried;
    }

    internal sealed class Named(Guid? organization, Guid? provider) : IRequestNamedOwner
    {
        public Guid? OrganizationId => organization;

        public Guid? ProviderId => provider;
    }

    internal static async Task<HttpRequestMessage> Send(IContext context)
    {
        var recorder = new Recorder();
        await Send(context, identity: null, recorder);
        return recorder.Sent!;
    }

    internal static async Task<HttpRequestMessage> Send(IContext context, IServiceIdentity identity)
    {
        var recorder = new Recorder();
        await Send(context, identity, recorder);
        return recorder.Sent!;
    }

    internal static async Task Send(IContext context, IServiceIdentity? identity, Recorder recorder)
    {
        var collection = new ServiceCollection().AddSingleton(context);
        if (identity is not null)
        {
            collection.AddSingleton(identity);
        }

        var services = collection.BuildServiceProvider();
        using var invoker = new HttpMessageInvoker(
            new ContextHandler(services.GetRequiredService<IServiceScopeFactory>(), new Signer()) { InnerHandler = recorder });

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://widget-service.invalid/api/v1/widgets");
        using var response = await invoker.SendAsync(request, Token);
    }

    internal static async Task<HttpRequestMessage> SendHandlingAnEvent(IContext context, bool refuse)
    {
        var recorder = new Recorder();
        var services = new ServiceCollection()
            .AddSingleton(context)
            .AddSingleton<IServiceIdentity>(new Identity(isProductService: false))
            .Configure<OutgoingCallOptions>(options => options.RefuseEventActor = refuse)
            .BuildServiceProvider();
        using var invoker = new HttpMessageInvoker(
            new ContextHandler(services.GetRequiredService<IServiceScopeFactory>(), new Signer()) { InnerHandler = recorder });

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://widget-service.invalid/api/v1/widgets");
        using var response = await invoker.SendAsync(request, Token);

        return recorder.Sent!;
    }

    internal sealed class AsyncRecorder : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal static async Task<HttpRequestMessage> Signed(IContext context, Signer signer, string? proof)
    {
        var recorder = new Recorder();
        var services = new ServiceCollection()
            .AddSingleton(context)
            .AddSingleton<ICallerProof>(new Proof(proof))
            .BuildServiceProvider();
        using var invoker = new HttpMessageInvoker(
            new ContextHandler(services.GetRequiredService<IServiceScopeFactory>(), signer) { InnerHandler = recorder });
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://widget-service.invalid/api/v1/widgets");

        using var response = await invoker.SendAsync(request, Token);
        return recorder.Sent!;
    }

    internal sealed class Proof(string? token) : ICallerProof
    {
        public string? SubjectToken => token;
    }

    /// <summary>
    /// Stands in for Identity's exchange: records what it was asked, answers a numbered token.
    /// </summary>
    internal sealed class Signer(bool issues = true) : IServiceTokenProvider
    {
        public List<(ContextAssertion Assertion, string? Subject)> Asked { get; } = [];

        public Task<string> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult("service-token");

        public Task<string?> GetContextTokenAsync(ContextAssertion assertion, string? subjectToken, CancellationToken cancellationToken = default)
        {
            Asked.Add((assertion, subjectToken));
            return Task.FromResult(issues ? $"signed-{Asked.Count}" : null);
        }
    }

    internal sealed class Identity(bool isProductService) : IServiceIdentity
    {
        public string Name => "some-service";

        public string Title => "Some Service";

        public bool IsProductService => isProductService;
    }

    internal sealed class Recorder : HttpMessageHandler
    {
        public HttpRequestMessage? Sent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
