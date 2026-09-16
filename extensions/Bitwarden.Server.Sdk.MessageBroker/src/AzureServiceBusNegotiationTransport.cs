using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Azure Service Bus implementation of <see cref="INegotiationTransport"/>. Uses session-enabled
/// subscriptions on the shared <see cref="NegotiationOptions.ControlTopicName"/> topic:
/// publisher-control keys each session by data-topic name (satisfies single-active-consumer
/// per data-topic); the per-service reply subscription keys each session by requester instance
/// identifier (routes each reply to the requester holding that session lock).
/// <para>
/// Requires the following pre-provisioned entities: the control topic; a session-enabled
/// <see cref="NegotiationOptions.PublisherControlSubscriptionName"/> subscription with a SQL
/// filter on the <c>data-topic</c> property matching this service's publish set; a
/// session-enabled <see cref="NegotiationOptions.ReplySubscriptionName"/> subscription with a
/// SQL filter on <c>To = '{ReplySubscriptionName}'</c>.
/// </para>
/// </summary>
internal sealed class AzureServiceBusNegotiationTransport : INegotiationTransport, IAsyncDisposable
{
    private const string DataTopicPropertyName = "data-topic";
    private const string CapabilitySubject = "capability";
    private const string JoinSubject = "join";

    private readonly NegotiationOptions _options;
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;

    private readonly ConcurrentDictionary<string, TaskCompletionSource<NegotiationAck>> _pendingReplies = new();
    private readonly CancellationTokenSource _replyLoopShutdown = new();
    private readonly SemaphoreSlim _replyInitLock = new(1, 1);
    private Task? _replyLoopTask;

    public AzureServiceBusNegotiationTransport(
        IOptions<MessagingOptions> messagingOptions,
        IOptions<NegotiationOptions> negotiationOptions)
    {
        _options = negotiationOptions.Value;
        var connectionString = messagingOptions.Value.AzureServiceBusConnectionString
            ?? throw new InvalidOperationException(
                $"{nameof(MessagingOptions.AzureServiceBusConnectionString)} is not configured.");
        _client = new ServiceBusClient(connectionString);
        _sender = _client.CreateSender(_options.ControlTopicName);
    }

    public Task<NegotiationAck> SendCapabilityAsync(Capability capability, CancellationToken cancellationToken = default)
        => SendAndAwaitAsync(
            capability.DataTopic,
            JsonSerializer.SerializeToUtf8Bytes(capability, NegotiationJsonContext.Default.Capability),
            CapabilitySubject,
            cancellationToken);

    public Task<NegotiationAck> SendJoinAsync(PublisherJoin join, CancellationToken cancellationToken = default)
        => SendAndAwaitAsync(
            join.DataTopic,
            JsonSerializer.SerializeToUtf8Bytes(join, NegotiationJsonContext.Default.PublisherJoin),
            JoinSubject,
            cancellationToken);

    private async Task<NegotiationAck> SendAndAwaitAsync(
        string dataTopic,
        byte[] body,
        string subject,
        CancellationToken cancellationToken)
    {
        await EnsureReplyLoopAsync(cancellationToken);

        var messageId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<NegotiationAck>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingReplies[messageId] = tcs;

        try
        {
            var msg = new ServiceBusMessage(body)
            {
                MessageId = messageId,
                Subject = subject,
                // SessionId partitions the pub-<service> subscription by data-topic so the
                // broker's session lock provides single-active-consumer semantics per topic,
                // which is needed to ensure a single writer to the negotiation state cache.
                SessionId = dataTopic,
                ReplyTo = _options.ReplySubscriptionName,
                ReplyToSessionId = _options.InstanceId,
            };
            msg.ApplicationProperties[DataTopicPropertyName] = dataTopic;

            await _sender.SendMessageAsync(msg, cancellationToken);

            using var timeoutCts = new CancellationTokenSource(_options.AdmissionTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            return await tcs.Task.WaitAsync(linkedCts.Token);
        }
        finally
        {
            _pendingReplies.TryRemove(messageId, out _);
        }
    }

    private async Task EnsureReplyLoopAsync(CancellationToken cancellationToken)
    {
        if (_replyLoopTask is not null) return;

        await _replyInitLock.WaitAsync(cancellationToken);
        try
        {
            if (_replyLoopTask is not null) return;

            var receiver = await _client.AcceptSessionAsync(
                _options.ControlTopicName,
                _options.ReplySubscriptionName,
                _options.InstanceId,
                cancellationToken: cancellationToken);

            _replyLoopTask = Task.Run(() => ReplyLoopAsync(receiver, _replyLoopShutdown.Token), CancellationToken.None);
        }
        finally
        {
            _replyInitLock.Release();
        }
    }

    private async Task ReplyLoopAsync(ServiceBusSessionReceiver receiver, CancellationToken cancellationToken)
    {
        await using (receiver)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ServiceBusReceivedMessage? msg;
                try
                {
                    msg = await receiver.ReceiveMessageAsync(cancellationToken: cancellationToken);
                }
                catch (OperationCanceledException) { break; }
                catch (ServiceBusException) { break; }

                if (msg is null) continue;

                try
                {
                    if (msg.CorrelationId is { } correlationId
                        && _pendingReplies.TryGetValue(correlationId, out var tcs))
                    {
                        var ack = JsonSerializer.Deserialize(msg.Body.ToArray(), NegotiationJsonContext.Default.NegotiationAck);
                        if (ack is not null) tcs.TrySetResult(ack);
                    }
                    await receiver.CompleteMessageAsync(msg, cancellationToken);
                }
                catch
                {
                    // Swallow to keep the loop alive; pending sends will time out.
                }
            }
        }
    }

    public async IAsyncEnumerable<INegotiationRequest> ReceiveRequestsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ServiceBusSessionReceiver receiver;
            try
            {
                receiver = await _client.AcceptNextSessionAsync(
                    _options.ControlTopicName,
                    _options.PublisherControlSubscriptionName,
                    cancellationToken: cancellationToken);
            }
            catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.ServiceTimeout)
            {
                continue;
            }

            await using (receiver)
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var msg = await receiver.ReceiveMessageAsync(cancellationToken: cancellationToken);
                    if (msg is null) break;

                    var request = BuildRequest(msg);
                    if (request is null)
                    {
                        await receiver.DeadLetterMessageAsync(msg, cancellationToken: cancellationToken);
                        continue;
                    }

                    yield return request;
                    await receiver.CompleteMessageAsync(msg, cancellationToken);
                }
            }
        }
    }

    private INegotiationRequest? BuildRequest(ServiceBusReceivedMessage msg)
    {
        try
        {
            return msg.Subject switch
            {
                CapabilitySubject => new CapabilityRequest
                {
                    Capability = JsonSerializer.Deserialize(msg.Body.ToArray(), NegotiationJsonContext.Default.Capability)
                        ?? throw new JsonException("null capability body"),
                    Replier = (ack, ct) => SendReplyAsync(msg, ack, ct),
                },
                JoinSubject => new JoinRequest
                {
                    Join = JsonSerializer.Deserialize(msg.Body.ToArray(), NegotiationJsonContext.Default.PublisherJoin)
                        ?? throw new JsonException("null join body"),
                    Replier = (ack, ct) => SendReplyAsync(msg, ack, ct),
                },
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Task SendReplyAsync(ServiceBusReceivedMessage original, NegotiationAck ack, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(original.ReplyTo))
            return Task.CompletedTask;

        var reply = new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(ack, NegotiationJsonContext.Default.NegotiationAck))
        {
            CorrelationId = original.MessageId,
            To = original.ReplyTo,
            SessionId = original.ReplyToSessionId,
        };
        return _sender.SendMessageAsync(reply, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _replyLoopShutdown.CancelAsync();
        if (_replyLoopTask is not null)
        {
            try { await _replyLoopTask; } catch { }
        }
        await _sender.DisposeAsync();
        await _client.DisposeAsync();
        _replyLoopShutdown.Dispose();
        _replyInitLock.Dispose();
    }
}
