using Azure.Core;
using Azure.Identity;
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
    private readonly CancellationTokenSource _closed = new();
    private volatile bool _isClosing;

    public ServiceBusClient Client { get; }

    /// <summary>
    /// True once the connection has started to close. Subscribers read it to tell a shutdown from
    /// a broker failure, whatever the Azure SDK surfaces while the client closes.
    /// </summary>
    public bool IsClosing => _isClosing;

    /// <summary>
    /// Cancelled after the client is disposed. Closing the client ends a receive that is waiting
    /// for a message, but not one still opening its link; subscribers stop waiting for a receive
    /// once this is cancelled.
    /// </summary>
    public CancellationToken Closed => _closed.Token;

    /// <param name="options">The connection string, or the namespace to reach with a token credential.</param>
    /// <param name="credential">
    /// The credential for a namespace, if the app registered one under
    /// <see cref="MessageBrokerKeys.Credential"/>; otherwise <see cref="DefaultAzureCredential"/> is
    /// used. Not used with a connection string.
    /// </param>
    public AzureServiceBusConnection(
        IOptions<MessagingOptions> options,
        [FromKeyedServices(MessageBrokerKeys.Credential)] TokenCredential? credential = null)
    {
        var value = options.Value;
        if (!string.IsNullOrEmpty(value.AzureServiceBusConnectionString))
        {
            Client = new ServiceBusClient(value.AzureServiceBusConnectionString);
            return;
        }
        Credential = credential ?? new DefaultAzureCredential();
        Client = new ServiceBusClient(value.AzureServiceBusFullyQualifiedNamespace, Credential);
    }

    /// <summary>The credential the client was built with, or null when it uses a connection string.</summary>
    internal TokenCredential? Credential { get; }

    public async ValueTask DisposeAsync()
    {
        _isClosing = true;

        // Dispose the client before cancelling: a receiver that closes while the client is still
        // open waits on its link, and the client's own close would then leave it waiting.
        await Client.DisposeAsync();

        // _closed is never disposed: a subscriber that starts after the connection closed still
        // reads Closed, and a source with no timer holds nothing to release.
        _closed.Cancel();
    }
}
