using Bitwarden.Server.Sdk.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using RichardSzalay.MockHttp;

namespace Bitwarden.Server.Sdk.UnitTests.Features;

public class LaunchDarklyClientProviderTests
{
    [Fact]
    public async Task NullSdkKey_MakesNoNetworkRequests()
    {
        var mockHttp = new MockHttpMessageHandler();
        var requestCount = 0;
        mockHttp.Fallback.Respond(_ =>
        {
            Interlocked.Increment(ref requestCount);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        });

        var messageHandlerFactory = Substitute.For<IHttpMessageHandlerFactory>();
        messageHandlerFactory.CreateHandler("LaunchDarkly").Returns(mockHttp);

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "TestApp",
        });

        builder.Services.AddFeatureFlagValues([
            KeyValuePair.Create("my-flag", "true"),
        ]);
        builder.Services.AddFeatureFlagServices();
        builder.Services.AddSingleton(messageHandlerFactory);

        using var host = builder.Build();
        using var scope = host.Services.CreateScope();

        var featureService = scope.ServiceProvider.GetRequiredService<IFeatureService>();
        Assert.True(featureService.IsEnabled("my-flag"));

        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task WithSdkKey_HttpRequestsContainKey()
    {
        const string sdkKey = "fake-sdk-key";

        HttpRequestMessage? capturedRequest = null;
        var requestReceived = new TaskCompletionSource();
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.Fallback.Respond(req =>
        {
            capturedRequest = req;
            requestReceived.TrySetResult();
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        });

        var messageHandlerFactory = Substitute.For<IHttpMessageHandlerFactory>();
        messageHandlerFactory.CreateHandler("LaunchDarkly").Returns(mockHttp);

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "TestApp",
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "Features:LaunchDarkly:SdkKey", sdkKey },
        });
        builder.Services.AddFeatureFlagServices();
        builder.Services.AddSingleton(messageHandlerFactory);

        using var host = builder.Build();
        using var scope = host.Services.CreateScope();

        _ = scope.ServiceProvider.GetRequiredService<IFeatureService>();

        var firstToFinish = await Task.WhenAny(requestReceived.Task, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Same(requestReceived.Task, firstToFinish);

        Assert.NotNull(capturedRequest);
        Assert.Equal(sdkKey, capturedRequest.Headers.GetValues("Authorization").First());
    }
}
