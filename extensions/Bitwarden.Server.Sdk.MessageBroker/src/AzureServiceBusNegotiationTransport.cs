using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Azure Service Bus implementation of <see cref="INegotiationTransport"/>.
/// <para>
/// Uses session-enabled subscriptions on the shared <see cref="NegotiationOptions.ControlTopicName"/>
/// topic. The request subscription's session-id is the bound data-topic name — the broker's
/// session lock enforces single-active-consumer semantics per data-topic. The reply subscription's
/// session-id is this instance's identifier so each running process holds its own reply session.
/// </para>
/// <para>
/// Construct via <see cref="ForListener"/> or <see cref="ForSender"/> — the factory name pins
/// the role and keeps the send-only vs listener choice out of argument values. A listener
/// instance is bound to one data-topic (concurrency across data-topics comes from constructing
/// one listener per topic); a sender instance is data-topic-agnostic because the outgoing
/// message carries its own data-topic on <c>SessionId</c> and the reply loop keys off the
/// per-process instance id.
/// </para>
/// <para>
/// Requires the following pre-provisioned entities: the control topic; a session-enabled
/// <see cref="NegotiationOptions.RequestSubscriptionName"/> subscription with a SQL filter on
/// the <c>data-topic</c> property matching this service's publish set; a session-enabled
/// <see cref="NegotiationOptions.ReplySubscriptionName"/> subscription with a SQL filter on
/// <c>To = '{ReplySubscriptionName}'</c>.
/// </para>
/// </summary>
internal sealed class AzureServiceBusNegotiationTransport : INegotiationTransport
{
    private const string DataTopicPropertyName = "data-topic";
    private const string CapabilitySubject = "capability";
    private const string JoinSubject = "join";
    private const string SubscriberLeaveSubject = "subscriber-leave";
    private const string PublisherLeaveSubject = "publisher-leave";
    private static readonly TimeSpan SacRetryDelay = TimeSpan.FromSeconds(2);

    private readonly NegotiationOptions _options;
    // Non-null only for the listener role: the data-topic whose SAC session this transport holds.
    // Sender-role instances leave this null; ReceiveRequestsAsync requires it and throws if called
    // on a sender.
    private readonly string? _dataTopic;
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;

    private readonly ConcurrentDictionary<string, TaskCompletionSource<NegotiationAck>> _pendingReplies = new();
    private readonly CancellationTokenSource _replyLoopShutdown = new();
    private readonly SemaphoreSlim _replyInitLock = new(1, 1);
    private Task? _replyLoopTask;

    private AzureServiceBusNegotiationTransport(
        IOptions<MessagingOptions> messagingOptions,
        IOptions<NegotiationOptions> negotiationOptions,
        string? dataTopic)
    {
        _options = negotiationOptions.Value;
        _dataTopic = dataTopic;
        var connectionString = messagingOptions.Value.AzureServiceBusConnectionString
            ?? throw new InvalidOperationException(
                $"{nameof(MessagingOptions.AzureServiceBusConnectionString)} is not configured.");
        _client = new ServiceBusClient(connectionString);
        _sender = _client.CreateSender(_options.ControlTopicName);
    }

    /// <summary>
    /// Listener role. Binds this transport to <paramref name="dataTopic"/>; the SAC-holding
    /// listener consumes requests for that topic via <see cref="ReceiveRequestsAsync"/>.
    /// </summary>
    public static AzureServiceBusNegotiationTransport ForListener(
        IOptions<MessagingOptions> messagingOptions,
        IOptions<NegotiationOptions> negotiationOptions,
        string dataTopic)
        => new(messagingOptions, negotiationOptions, dataTopic);

    /// <summary>
    /// Send-only role — used by both publisher and subscriber requesters. Sending is
    /// data-topic-agnostic (the outgoing message carries its own data-topic on
    /// <c>SessionId</c>) and the reply loop keys off <see cref="NegotiationOptions.InstanceId"/>,
    /// so no per-topic binding is required at construction. <see cref="ReceiveRequestsAsync"/>
    /// is not valid on a sender instance.
    /// </summary>
    public static AzureServiceBusNegotiationTransport ForSender(
        IOptions<MessagingOptions> messagingOptions,
        IOptions<NegotiationOptions> negotiationOptions)
        => new(messagingOptions, negotiationOptions, dataTopic: null);

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
        var dataTopic = _dataTopic
            ?? throw new InvalidOperationException(
                $"{nameof(ReceiveRequestsAsync)} is not valid on a sender-role transport. " +
                $"Construct via {nameof(ForListener)} to bind a data-topic before calling.");

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
                    sessionId: dataTopic,
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

                    var request = BuildRequest(msg, dataTopic);
                    if (request is null)
                    {
                        await receiver.DeadLetterMessageAsync(msg, cancellationToken: cancellationToken);
                        continue;
                    }

                    yield return request;
                    // Settle with CancellationToken.None: if the listener is cancelling between
                    // handler completion and this call, we still want the message removed since
                    // the reply has already been sent and the state cache has already been updated.
                    // Aborting here would cause the broker to redeliver a message that we have
                    // already processed to completion.
                    await receiver.CompleteMessageAsync(msg, CancellationToken.None);
                }
            }
        }
    }

    private INegotiationInbound? BuildRequest(ServiceBusReceivedMessage msg, string dataTopic)
    {
        try
        {
            return msg.Subject switch
            {
                CapabilitySubject => BuildCapabilityRequest(msg, dataTopic),
                JoinSubject => BuildJoinRequest(msg, dataTopic),
                PublisherLeaveSubject => BuildPublisherLeaveNotification(msg, dataTopic),
                SubscriberLeaveSubject => BuildSubscriberLeaveNotification(msg, dataTopic),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private CapabilityRequest? BuildCapabilityRequest(ServiceBusReceivedMessage msg, string dataTopic)
    {
        var capability = JsonSerializer.Deserialize(msg.Body.ToArray(), NegotiationJsonContext.Default.Capability);
        // data-topic property is used as the filter downstream. We want to be sure it matches
        // the routing used to hit this transport
        return capability is not null && capability.DataTopic == dataTopic
            ? new CapabilityRequest { Capability = capability, Replier = (ack, ct) => SendReplyAsync(msg, ack, ct) }
            : null;
    }

    private JoinRequest? BuildJoinRequest(ServiceBusReceivedMessage msg, string dataTopic)
    {
        var join = JsonSerializer.Deserialize(msg.Body.ToArray(), NegotiationJsonContext.Default.PublisherJoin);
        // data-topic property is used as the filter downstream. We want to be sure it matches
        // the routing used to hit this transport
        return join is not null && join.DataTopic == dataTopic
            ? new JoinRequest { Join = join, Replier = (ack, ct) => SendReplyAsync(msg, ack, ct) }
            : null;
    }

    private PublisherLeaveNotification? BuildPublisherLeaveNotification(ServiceBusReceivedMessage msg, string dataTopic)
    {
        var leave = JsonSerializer.Deserialize(msg.Body.ToArray(), NegotiationJsonContext.Default.PublisherLeave);
        return leave is not null && leave.DataTopic == dataTopic
            ? new PublisherLeaveNotification { Leave = leave }
            : null;
    }

    private SubscriberLeaveNotification? BuildSubscriberLeaveNotification(ServiceBusReceivedMessage msg, string dataTopic)
    {
        var leave = JsonSerializer.Deserialize(msg.Body.ToArray(), NegotiationJsonContext.Default.SubscriberLeave);
        return leave is not null && leave.DataTopic == dataTopic
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
