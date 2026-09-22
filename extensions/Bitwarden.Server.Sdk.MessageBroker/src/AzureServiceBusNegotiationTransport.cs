using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Azure Service Bus implementation of <see cref="INegotiationTransport"/>. Each instance is
/// bound to one data-topic at construction. Concurrency across data-topics for one service
/// is achieved by constructing one transport per data-topic.
/// <para>
/// Uses session-enabled subscriptions on the shared <see cref="NegotiationOptions.ControlTopicName"/>
/// topic. The request subscription's session-id is the bound data-topic name — the broker's
/// session lock enforces single-active-consumer semantics per data-topic. The reply subscription's
/// session-id is this instance's identifier so each running process holds its own reply session.
/// </para>
/// <para>
/// Requires the following pre-provisioned entities: the control topic; a session-enabled
/// <see cref="NegotiationOptions.RequestSubscriptionName"/> subscription with a SQL filter on
/// the <c>data-topic</c> property matching this service's publish set; a session-enabled
/// <see cref="NegotiationOptions.ReplySubscriptionName"/> subscription with a SQL filter on
/// <c>To = '{ReplySubscriptionName}'</c>.
/// </para>
/// </summary>
internal sealed class AzureServiceBusNegotiationTransport : INegotiationTransport, IAsyncDisposable
{
    private const string DataTopicPropertyName = "data-topic";
    private const string CapabilitySubject = "capability";
    private const string JoinSubject = "join";
    private const string SubscriberLeaveSubject = "subscriber-leave";
    private const string PublisherLeaveSubject = "publisher-leave";
    private static readonly TimeSpan SacRetryDelay = TimeSpan.FromSeconds(2);

    private readonly NegotiationOptions _options;
    private readonly string _dataTopic;
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;

    private readonly ConcurrentDictionary<string, TaskCompletionSource<NegotiationAck>> _pendingReplies = new();
    private readonly CancellationTokenSource _replyLoopShutdown = new();
    private readonly SemaphoreSlim _replyInitLock = new(1, 1);
    private Task? _replyLoopTask;

    public AzureServiceBusNegotiationTransport(
        IOptions<MessagingOptions> messagingOptions,
        IOptions<NegotiationOptions> negotiationOptions,
        string dataTopic)
    {
        _options = negotiationOptions.Value;
        _dataTopic = dataTopic;
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

    public Task SendPublisherLeaveAsync(PublisherLeave leave, CancellationToken cancellationToken = default)
        => SendFireAndForgetAsync(
            leave.DataTopic,
            JsonSerializer.SerializeToUtf8Bytes(leave, NegotiationJsonContext.Default.PublisherLeave),
            PublisherLeaveSubject,
            cancellationToken);

    public Task SendSubscriberLeaveAsync(SubscriberLeave leave, CancellationToken cancellationToken = default)
        => SendFireAndForgetAsync(
            leave.DataTopic,
            JsonSerializer.SerializeToUtf8Bytes(leave, NegotiationJsonContext.Default.SubscriberLeave),
            SubscriberLeaveSubject,
            cancellationToken);

    private Task SendFireAndForgetAsync(
        string dataTopic,
        byte[] body,
        string subject,
        CancellationToken cancellationToken)
    {
        var msg = new ServiceBusMessage(body)
        {
            Subject = subject,
            // SessionId partitions the request subscription by data-topic so the SAC holder for
            // this topic receives the leave.
            SessionId = dataTopic,
            // No MessageId + no ReplyTo — the listener sees no reply address and skips replying.
        };
        msg.ApplicationProperties[DataTopicPropertyName] = dataTopic;
        return _sender.SendMessageAsync(msg, cancellationToken);
    }

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
                // SessionId partitions the request-<service> subscription by data-topic so the
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

    public async IAsyncEnumerable<INegotiationInbound> ReceiveRequestsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ServiceBusSessionReceiver receiver;
            try
            {
                // If another instance of the same service already holds the SAC role for this topic,
                // the broker throws either SessionCannotBeLocked (immediate) or ServiceTimeout
                // (after the client's TryTimeout).
                // Either way we sleep briefly and retry so we take over once the holder shuts down or dies.
                receiver = await _client.AcceptSessionAsync(
                    _options.ControlTopicName,
                    _options.RequestSubscriptionName,
                    sessionId: _dataTopic,
                    cancellationToken: cancellationToken);
            }
            catch (ServiceBusException ex) when (ex.Reason is ServiceBusFailureReason.SessionCannotBeLocked
                                                            or ServiceBusFailureReason.ServiceTimeout)
            {
                await Task.Delay(SacRetryDelay, cancellationToken);
                continue;
            }

            await using (receiver)
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    ServiceBusReceivedMessage? msg;
                    try
                    {
                        // The SDK auto-renews our session lock during a blocked ReceiveMessageAsync,
                        // so no explicit renewal is needed. A null return is a long-poll timeout —
                        // stay on the session rather than releasing (we are the active consumer).
                        msg = await receiver.ReceiveMessageAsync(cancellationToken: cancellationToken);
                    }
                    catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.SessionLockLost)
                    {
                        // Broker lost our lock (network partition, renewal miss). Exit the inner
                        // loop and reacquire from scratch via the outer AcceptSessionAsync.
                        break;
                    }
                    if (msg is null) continue;

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

    private INegotiationInbound? BuildRequest(ServiceBusReceivedMessage msg)
    {
        try
        {
            return msg.Subject switch
            {
                CapabilitySubject => BuildCapabilityRequest(msg),
                JoinSubject => BuildJoinRequest(msg),
                PublisherLeaveSubject => BuildPublisherLeaveNotification(msg),
                SubscriberLeaveSubject => BuildSubscriberLeaveNotification(msg),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private CapabilityRequest? BuildCapabilityRequest(ServiceBusReceivedMessage msg)
    {
        var capability = JsonSerializer.Deserialize(msg.Body.ToArray(), NegotiationJsonContext.Default.Capability);
        // data-topic property is used as the filter downstream. We want to be sure it matches
        // the routing used to hit this transport
        return capability is not null && capability.DataTopic == _dataTopic
            ? new CapabilityRequest { Capability = capability, Replier = (ack, ct) => SendReplyAsync(msg, ack, ct) }
            : null;
    }

    private JoinRequest? BuildJoinRequest(ServiceBusReceivedMessage msg)
    {
        var join = JsonSerializer.Deserialize(msg.Body.ToArray(), NegotiationJsonContext.Default.PublisherJoin);
        // data-topic property is used as the filter downstream. We want to be sure it matches
        // the routing used to hit this transport
        return join is not null && join.DataTopic == _dataTopic
            ? new JoinRequest { Join = join, Replier = (ack, ct) => SendReplyAsync(msg, ack, ct) }
            : null;
    }

    private PublisherLeaveNotification? BuildPublisherLeaveNotification(ServiceBusReceivedMessage msg)
    {
        var leave = JsonSerializer.Deserialize(msg.Body.ToArray(), NegotiationJsonContext.Default.PublisherLeave);
        return leave is not null && leave.DataTopic == _dataTopic
            ? new PublisherLeaveNotification { Leave = leave }
            : null;
    }

    private SubscriberLeaveNotification? BuildSubscriberLeaveNotification(ServiceBusReceivedMessage msg)
    {
        var leave = JsonSerializer.Deserialize(msg.Body.ToArray(), NegotiationJsonContext.Default.SubscriberLeave);
        return leave is not null && leave.DataTopic == _dataTopic
            ? new SubscriberLeaveNotification { Leave = leave }
            : null;
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
