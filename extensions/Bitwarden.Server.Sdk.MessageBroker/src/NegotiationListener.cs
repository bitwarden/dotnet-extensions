namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Consumes and answers incoming negotiation requests delivered by an
/// <see cref="INegotiationTransport"/>: a subscriber's <see cref="Capability"/> is admitted
/// against every cached publisher for the same data-topic; a booting publisher's
/// <see cref="PublisherJoin"/> is admitted against every cached subscriber. Admission passes
/// when the requester's wire-name set overlaps every counterparty's set; otherwise a no-go
/// reply lists the incompatible parties.
/// <para>
/// The listener relies on the transport to enforce single-active-consumer semantics per
/// data-topic: no explicit locking here. If two instances of this listener process the same
/// data-topic concurrently, the last cache write wins and admission decisions can race.
/// </para>
/// <para>
/// Lifetime is managed by <see cref="NegotiationListenerCoordinator"/>, which starts each
/// listener via <see cref="RunAsync"/> in a background task and drives shutdown via
/// cancellation.
/// </para>
/// </summary>
internal sealed class NegotiationListener
{
    private readonly INegotiationTransport _transport;
    private readonly INegotiationState _state;

    public NegotiationListener(INegotiationTransport transport, INegotiationState state)
    {
        _transport = transport;
        _state = state;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var request in _transport.ReceiveRequestsAsync(cancellationToken))
        {
            switch (request)
            {
                case CapabilityRequest cap:
                    await HandleCapabilityAsync(cap, cancellationToken);
                    break;
                case JoinRequest join:
                    await HandleJoinAsync(join, cancellationToken);
                    break;
                case PublisherLeaveNotification pln:
                    await _state.RemovePublisherAsync(pln.Leave.DataTopic, pln.Leave.InstanceId, cancellationToken);
                    break;
                case SubscriberLeaveNotification sln:
                    await _state.RemoveSubscriberAsync(sln.Leave.DataTopic, sln.Leave.InstanceId, cancellationToken);
                    break;
            }
        }
    }

    private async Task HandleCapabilityAsync(CapabilityRequest request, CancellationToken cancellationToken)
    {
        var capability = request.Capability;

        var existing = await _state.TryGetSubscriberAsync(capability.DataTopic, capability.InstanceId, cancellationToken);
        if (existing is not null && existing.WireNames.SetEquals(capability.WireNames))
        {
            await _state.UpsertSubscriberAsync(capability.DataTopic, capability, cancellationToken);
            await request.ReplyAsync(new NegotiationAck { Go = true }, cancellationToken);
            return;
        }

        var offenders = new List<NegotiationIncompatibility>();
        await foreach (var publisher in _state.GetPublishersAsync(capability.DataTopic, cancellationToken))
        {
            if (!capability.WireNames.Overlaps(publisher.WireNames))
            {
                offenders.Add(new NegotiationIncompatibility
                {
                    InstanceId = publisher.InstanceId,
                    WireNames = publisher.WireNames,
                });
            }
        }

        if (offenders.Count > 0)
        {
            await request.ReplyAsync(new NegotiationAck { Go = false, Offenders = offenders }, cancellationToken);
            return;
        }

        await _state.UpsertSubscriberAsync(capability.DataTopic, capability, cancellationToken);
        await request.ReplyAsync(new NegotiationAck { Go = true }, cancellationToken);
    }

    private async Task HandleJoinAsync(JoinRequest request, CancellationToken cancellationToken)
    {
        var join = request.Join;

        var existing = await _state.TryGetPublisherAsync(join.DataTopic, join.InstanceId, cancellationToken);
        if (existing is not null && existing.WireNames.SetEquals(join.WireNames))
        {
            await _state.UpsertPublisherAsync(join.DataTopic, join, cancellationToken);
            await request.ReplyAsync(new NegotiationAck { Go = true }, cancellationToken);
            return;
        }

        var offenders = new List<NegotiationIncompatibility>();
        await foreach (var subscriber in _state.GetSubscribersAsync(join.DataTopic, cancellationToken))
        {
            if (!join.WireNames.Overlaps(subscriber.WireNames))
            {
                offenders.Add(new NegotiationIncompatibility
                {
                    InstanceId = subscriber.InstanceId,
                    WireNames = subscriber.WireNames,
                });
            }
        }

        if (offenders.Count > 0)
        {
            await request.ReplyAsync(new NegotiationAck { Go = false, Offenders = offenders }, cancellationToken);
            return;
        }

        await _state.UpsertPublisherAsync(join.DataTopic, join, cancellationToken);
        await request.ReplyAsync(new NegotiationAck { Go = true }, cancellationToken);
    }
}
