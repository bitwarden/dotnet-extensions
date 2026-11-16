using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

/// <summary>
/// Azure Service Bus by connection string (a shared access key) or by namespace (a token
/// credential the app registers, typically its managed identity). Nothing here connects: building a client is enough
/// to check which way it was configured.
/// </summary>
public class AzureServiceBusConfigurationTests
{
    private const string Namespace = "bitwarden-test.servicebus.windows.net";

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddMetrics();
        services.AddLogging();
        services.AddPublisher<MyItemPayload, MyItem>("test");
        services.AddOptions<MessagingOptions>().BindConfiguration("");
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ANamespaceSelectsAzureServiceBusWithTheRegisteredCredential()
    {
        var credential = new RecordingCredential();
        await using var provider = BuildProvider(
            new() { ["AzureServiceBusFullyQualifiedNamespace"] = Namespace },
            services => services.AddKeyedSingleton<TokenCredential>(MessageBrokerKeys.Credential, credential));

        var connection = provider.GetRequiredService<AzureServiceBusConnection>();
        var publisher = provider.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(MessageBrokerKeys.Publisher<MyItemPayload>("test"));

        Assert.Equal(Namespace, connection.Client.FullyQualifiedNamespace);
        Assert.Same(credential, connection.Credential);
        Assert.StartsWith("AzureServiceBusPublisher", publisher.GetType().Name);
    }

    [Fact]
    public async Task ANamespaceWithNoRegisteredCredentialIsRefused()
    {
        await using var provider = BuildProvider(new() { ["AzureServiceBusFullyQualifiedNamespace"] = Namespace });

        var refused = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<MessagingOptions>>().Value);
        Assert.Contains(nameof(MessageBrokerKeys.Credential), refused.Message);
    }

    [Fact]
    public async Task AnUnkeyedCredentialRegisteredForSomethingElseIsNotTaken()
    {
        var credential = new RecordingCredential();
        await using var provider = BuildProvider(
            new() { ["AzureServiceBusFullyQualifiedNamespace"] = Namespace },
            services => services.AddSingleton<TokenCredential>(credential));

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<MessagingOptions>>().Value);
    }

    [Fact]
    public async Task AConnectionStringStillSelectsAzureServiceBus()
    {
        await using var provider = BuildProvider(new()
        {
            ["AzureServiceBusConnectionString"] = $"Endpoint=sb://{Namespace}/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==",
        });

        var connection = provider.GetRequiredService<AzureServiceBusConnection>();

        Assert.Equal(Namespace, connection.Client.FullyQualifiedNamespace);
        Assert.Null(connection.Credential);
    }

    [Fact]
    public void AConnectionStringAndANamespaceTogetherAreRefused()
    {
        var result = new MessagingOptionsValidator().Validate(null, new MessagingOptions
        {
            AzureServiceBusConnectionString = $"Endpoint=sb://{Namespace}/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==",
            AzureServiceBusFullyQualifiedNamespace = Namespace,
        });

        Assert.True(result.Failed);
    }

    [Fact]
    public void ANamespaceAndRabbitTogetherAreRefused()
    {
        var result = new MessagingOptionsValidator().Validate(null, new MessagingOptions
        {
            AzureServiceBusFullyQualifiedNamespace = Namespace,
            RabbitUri = "amqp://localhost/",
        });

        Assert.True(result.Failed);
    }

    private sealed class RecordingCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
