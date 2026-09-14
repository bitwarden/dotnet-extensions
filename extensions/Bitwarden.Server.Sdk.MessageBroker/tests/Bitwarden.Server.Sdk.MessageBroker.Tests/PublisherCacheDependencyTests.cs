using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

[Collection("InMemory")]
public class PublisherCacheDependencyTests
{
    /// <summary>
    /// The in-memory channel backend is single-process; variant negotiation across processes is
    /// impossible, so no shared cache is required. Publisher resolution must succeed even when
    /// no caching is registered.
    /// </summary>
    [Fact]
    public void ChannelPublisherResolvesWithoutDistributedCache()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddLogging();
        services.AddPublisher<MyItemPayload, MyItem>("test");
        var provider = services.BuildServiceProvider();

        var publisher = provider.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>("test");
        Assert.NotNull(publisher);
    }

    /// <summary>
    /// The <see cref="Microsoft.Extensions.DependencyInjection.PublisherCacheValidator"/> fires
    /// only when a distributed backend is configured AND no caching is registered anywhere.
    /// It names the fix rather than letting a cryptic resolution failure surface downstream.
    /// </summary>
    [Fact]
    public void ValidatorFailsWhenDistributedBackendIsConfiguredWithoutAnyCaching()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddLogging();
        services.AddPublisher<MyItemPayload, MyItem>("test");
        services.Configure<MessagingOptions>(o => o.RabbitUri = "amqp://guest:guest@localhost/");
        var provider = services.BuildServiceProvider();

        // Invoke validators against a hand-built options instance so we can inspect their
        // ValidateOptionsResult individually rather than tripping OptionsValidationException
        // from IOptions.Value.
        var options = new MessagingOptions { RabbitUri = "amqp://guest:guest@localhost/" };
        var results = provider.GetServices<IValidateOptions<MessagingOptions>>()
            .Select(v => v.Validate(null, options))
            .ToList();

        var cacheFailure = results.FirstOrDefault(r => r.Failed
            && r.FailureMessage != null
            && r.FailureMessage.Contains("AddBitwardenCaching", StringComparison.Ordinal));
        Assert.NotNull(cacheFailure);
    }
}
