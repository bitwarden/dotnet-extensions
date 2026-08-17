using System.Buffers;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers per-subscription escrow callbacks with <see cref="ChannelTopic{T}"/> so that
/// <see cref="ChannelTopic{T}"/> handles startup recovery and shutdown drain as part of its own
/// <see cref="Microsoft.Extensions.Hosting.IHostedService"/> lifecycle — no separate hosted service
/// is required. Registered by
/// <see cref="MessageBrokerServiceCollectionExtensions.AddMessageConsumer{T,TConsumer}"/> for the
/// in-memory channel backend; no-ops when Azure Service Bus or RabbitMQ is configured.
/// </summary>
internal sealed class ChannelEscrowRegistration<T>
{
    private readonly string _subscriptionName;
    private readonly string _subscriptionKey;
    private readonly IMessageSerializer _serializer;
    private readonly IOptions<MessagingOptions> _messagingOptions;
    private readonly IMessageEscrowStore? _primaryStore;
    private readonly ILogger<ChannelEscrowRegistration<T>> _logger;

    public string TopicName { get; }

    public ChannelEscrowRegistration(
        string topicName,
        string subscriptionName,
        string subscriptionKey,
        IMessageSerializer serializer,
        IOptions<MessagingOptions> messagingOptions,
        IMessageEscrowStore? primaryStore,
        ILogger<ChannelEscrowRegistration<T>> logger)
    {
        TopicName = topicName;
        _subscriptionName = subscriptionName;
        _subscriptionKey = subscriptionKey;
        _serializer = serializer;
        _messagingOptions = messagingOptions;
        _primaryStore = primaryStore;
        _logger = logger;
    }

    /// <summary>
    /// Registers startup recovery, shutdown drain, and mid-flight abandon callbacks with
    /// <paramref name="topic"/> for this subscription.
    /// </summary>
    public void RegisterWith(ChannelTopic<T> topic)
    {
        topic.SetStartupRecovery(_subscriptionName, StartupRecoveryAsync);
        topic.SetShutdownDrain(_subscriptionName, ShutdownDrainAsync);
        topic.SetEscrowFallback(_subscriptionName, EscrowDirectAsync);
    }

    private async Task StartupRecoveryAsync(ChannelWriter<Envelope<T>> writer, CancellationToken cancellationToken)
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

            await writer.WriteAsync(
                new ChannelEnvelope<T>(writer, EscrowDirectAsync, message, entry.MessageId, entry.TraceId, entry.DeliveryCount, maxDeliveryCount, _logger, TopicName),
                cancellationToken);
        }

        _logger.LogInformation("Recovered {Count} escrowed messages for {Key}.", escrowed.Count, _subscriptionKey);
    }

    private async Task ShutdownDrainAsync(IReadOnlyList<Envelope<T>> messages, CancellationToken cancellationToken)
    {
        if (IsExternalBackend())
            return;

        var escrowed = new List<EscrowedMessage>(messages.Count);
        foreach (var envelope in messages)
        {
            try
            {
                var payloadWriter = new ArrayBufferWriter<byte>();
                _serializer.Serialize(envelope.Message, payloadWriter);
                escrowed.Add(new EscrowedMessage(envelope.MessageId, envelope.TraceId, envelope.DeliveryCount, payloadWriter.WrittenMemory.ToArray()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to serialize message {MessageId} for escrow in {Key}; discarding.",
                    envelope.MessageId, _subscriptionKey);
            }
        }

        if (escrowed.Count == 0)
            return;

        if (_primaryStore is not null)
        {
            try
            {
                await _primaryStore.WriteAsync(_subscriptionKey, escrowed, cancellationToken);
                _logger.LogInformation("Escrowed {Count} messages for {Key}.", escrowed.Count, _subscriptionKey);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to write {Count} messages to escrow store for {Key}; falling back to log.",
                    escrowed.Count, _subscriptionKey);
            }
        }

        foreach (var m in escrowed)
            _logger.LogError("Undelivered message escrowed to log for {Key}: {MessageId}",
                _subscriptionKey, m.MessageId);
    }

    // Safety net: called when RequeueCoreAsync cannot write back to the channel because the
    // writer is already closed. Writes the single message straight to the store so it is not lost.
    private async Task EscrowDirectAsync(Envelope<T> envelope, CancellationToken cancellationToken)
    {
        if (IsExternalBackend())
            return;

        byte[] payload;
        try
        {
            var payloadWriter = new ArrayBufferWriter<byte>();
            _serializer.Serialize(envelope.Message, payloadWriter);
            payload = payloadWriter.WrittenMemory.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to serialize message {MessageId} for direct escrow in {Key}; message will be lost.",
                envelope.MessageId, _subscriptionKey);
            return;
        }

        var escrowed = new EscrowedMessage(envelope.MessageId, envelope.TraceId, envelope.DeliveryCount, payload);

        if (_primaryStore is not null)
        {
            try
            {
                await _primaryStore.WriteAsync(_subscriptionKey, [escrowed], cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to write message {MessageId} to escrow store for {Key}; message will be lost.",
                    envelope.MessageId, _subscriptionKey);
            }
        }
        else
        {
            _logger.LogError("Undelivered message escrowed to log for {Key}: {MessageId}",
                _subscriptionKey, envelope.MessageId);
        }
    }

    private bool IsExternalBackend()
    {
        var opts = _messagingOptions.Value;
        return !string.IsNullOrEmpty(opts.AzureServiceBusConnectionString) || !string.IsNullOrEmpty(opts.RabbitUri);
    }
}
