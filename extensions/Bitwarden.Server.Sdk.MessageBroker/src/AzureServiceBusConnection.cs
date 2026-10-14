using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Owns the shared <see cref="ServiceBusClient"/> for the lifetime of the host. Wrapped as an
/// internal type so this package does not register the Azure SDK's <see cref="ServiceBusClient"/>
/// into the consuming app's container, where it could collide with the app's own Service Bus
/// client registration.
/// </summary>
internal sealed class AzureServiceBusConnection : IAsyncDisposable
{
    public ServiceBusClient Client { get; }

    public AzureServiceBusConnection(IOptions<MessagingOptions> options)
    {
        Client = new ServiceBusClient(options.Value.AzureServiceBusConnectionString);
    }

    public ValueTask DisposeAsync() => Client.DisposeAsync();
}
