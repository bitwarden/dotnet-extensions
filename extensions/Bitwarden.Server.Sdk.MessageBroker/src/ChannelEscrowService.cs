using System.Buffers;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Recovers escrowed messages into the channel at startup and drains any undelivered channel
/// messages to the escrow store at shutdown. Registered by
/// <see cref="MessageBrokerServiceCollectionExtensions.AddMessageConsumer{T,TConsumer}"/> for the
/// in-memory channel backend; no-ops when Azure Service Bus or RabbitMQ is configured.
/// </summary>
internal sealed class ChannelEscrowService<T> : IHostedService
{
    private readonly Channel<Envelope<T>> _channel;
    private readonly string _subscriptionKey;
    private readonly string _topicName;
    private readonly IMessageSerializer _serializer;
    private readonly IOptions<MessagingOptions> _messagingOptions;
    private readonly IMessageEscrowStore? _primaryStore;
    private readonly ILogger<ChannelEscrowService<T>> _logger;

    public ChannelEscrowService(
        Channel<Envelope<T>> channel,
        string subscriptionKey,
        string topicName,
        IMessageSerializer serializer,
        IOptions<MessagingOptions> messagingOptions,
        IMessageEscrowStore? primaryStore,
        ILogger<ChannelEscrowService<T>> logger)
    {
        _channel = channel;
        _subscriptionKey = subscriptionKey;
        _topicName = topicName;
        _serializer = serializer;
        _messagingOptions = messagingOptions;
        _primaryStore = primaryStore;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (IsExternalBackend() || _primaryStore is null)
            return;

        IReadOnlyList<EscrowedMessage> escrowed;
        try
        {
            escrowed = await _primaryStore.ReadAndClearAsync(_subscriptionKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read escrow for {Key}; messages will not be recovered.", _subscriptionKey);
            return;
        }

        if (escrowed.Count == 0)
            return;

        var maxDeliveryCount = _messagingOptions.Value.MaxDeliveryCount;

        foreach (var entry in escrowed)
        {
            T? message;
            try
            {
                message = _serializer.Deserialize<T>(entry.Payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize escrowed payload for message {MessageId} in {Key}; discarding.", entry.MessageId, _subscriptionKey);
                continue;
            }

            if (message is null)
            {
                _logger.LogWarning("Escrowed payload for message {MessageId} in {Key} deserialized to null; discarding.", entry.MessageId, _subscriptionKey);
                continue;
            }

            await _channel.Writer.WriteAsync(
                new ChannelEnvelope<T>(_channel.Writer, message, entry.MessageId, entry.TraceId, entry.DeliveryCount, maxDeliveryCount, _logger, _topicName),
                cancellationToken);
        }

        _logger.LogInformation("Recovered {Count} escrowed messages for {Key}.", escrowed.Count, _subscriptionKey);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (IsExternalBackend())
            return;

        var messages = new List<EscrowedMessage>();

        while (_channel.Reader.TryRead(out var envelope))
        {
            try
            {
                var payloadWriter = new ArrayBufferWriter<byte>();
                _serializer.Serialize(envelope.Message, payloadWriter);
                messages.Add(new EscrowedMessage(envelope.MessageId, envelope.TraceId, envelope.DeliveryCount, payloadWriter.WrittenMemory.ToArray()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to serialize message {MessageId} for escrow in {Key}; discarding.",
                    envelope.MessageId, _subscriptionKey);
            }
        }

        if (messages.Count == 0)
            return;

        if (_primaryStore is not null)
        {
            try
            {
                await _primaryStore.WriteAsync(_subscriptionKey, messages, cancellationToken);
                _logger.LogInformation("Escrowed {Count} messages for {Key}.", messages.Count, _subscriptionKey);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to write {Count} messages to escrow store for {Key}; falling back to log.",
                    messages.Count, _subscriptionKey);
            }
        }

        foreach (var m in messages)
            _logger.LogError("Undelivered message escrowed to log for {Key}: {MessageId} payload={Payload}",
                _subscriptionKey, m.MessageId, Convert.ToBase64String(m.Payload));
    }

    private bool IsExternalBackend()
    {
        var opts = _messagingOptions.Value;
        return !string.IsNullOrEmpty(opts.AzureServiceBusConnectionString) || !string.IsNullOrEmpty(opts.RabbitUri);
    }
}
