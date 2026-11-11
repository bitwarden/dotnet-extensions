namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Consumes and answers incoming negotiation requests pulled from an
/// <see cref="INegotiationListener"/> transport: admission is scoped to a single
/// <c>(data-topic, family)</c>. A subscriber's <see cref="Capability"/> is admitted against
/// every cached same-family publisher; a booting publisher's <see cref="PublisherJoin"/> is
/// admitted against every cached same-family subscriber. Admission passes when the requester's
/// wire-name set overlaps every counterparty's set; otherwise a no-go reply lists the
/// incompatible parties.
/// <para>
/// Relies on the transport to enforce single-active-consumer semantics per data-topic: no
/// explicit locking here. Multiple families on one topic share the same listener instance;
/// the per-family cache keys (plus SAC per topic) keep their state buckets independent.
/// </para>
/// <para>
/// Lifetime is managed by <see cref="NegotiationListenerCoordinator"/>, which starts each
/// listener via <see cref="RunAsync"/> in a background task and drives shutdown via
/// cancellation.
/// </para>
/// </summary>
internal sealed class NegotiationListener
{
    private readonly INegotiationListener _transport;
    private readonly INegotiationState _state;
    private readonly NegotiationMetrics _metrics;

    public NegotiationListener(INegotiationListener transport, INegotiationState state, NegotiationMetrics metrics)
    {
        _transport = transport;
        _state = state;
        _metrics = metrics;
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
                    await _state.RemovePublisherAsync(pln.Leave.DataTopic, pln.Leave.FamilyWireName, pln.Leave.InstanceId, cancellationToken);
                    break;
                case SubscriberLeaveNotification sln:
                    await _state.RemoveSubscriberAsync(sln.Leave.DataTopic, sln.Leave.FamilyWireName, sln.Leave.InstanceId, cancellationToken);
                    break;
            }
        }
    }

    private async Task HandleCapabilityAsync(CapabilityRequest request, CancellationToken cancellationToken)
    {
        var capability = request.Capability;

        var existing = await _state.TryGetSubscriberAsync(capability.DataTopic, capability.FamilyWireName, capability.InstanceId, cancellationToken);
        if (existing is not null && existing.WireNames.SetEquals(capability.WireNames))
        {
            await _state.UpsertSubscriberAsync(capability.DataTopic, capability, cancellationToken);
            _metrics.RecordCapabilityAdmission(capability.DataTopic, capability.FamilyWireName, go: true);
            await request.ReplyAsync(new NegotiationAck { Go = true }, cancellationToken);
            return;
        }

        var offenders = new List<NegotiationIncompatibility>();
        await foreach (var publisher in _state.GetPublishersAsync(capability.DataTopic, capability.FamilyWireName, cancellationToken))
        {
            if (!capability.WireNames.Overlaps(publisher.WireNames))
            {
                offenders.Add(new NegotiationIncompatibility
                {
                    InstanceId = publisher.InstanceId,
                    FamilyWireName = capability.FamilyWireName,
                    WireNames = publisher.WireNames,
                });
            }
        }

        if (offenders.Count > 0)
        {
            _metrics.RecordCapabilityAdmission(capability.DataTopic, capability.FamilyWireName, go: false);
            await request.ReplyAsync(new NegotiationAck { Go = false, Offenders = offenders }, cancellationToken);
            return;
        }

        await _state.UpsertSubscriberAsync(capability.DataTopic, capability, cancellationToken);
        _metrics.RecordCapabilityAdmission(capability.DataTopic, capability.FamilyWireName, go: true);
        await request.ReplyAsync(new NegotiationAck { Go = true }, cancellationToken);
    }

    private async Task HandleJoinAsync(JoinRequest request, CancellationToken cancellationToken)
    {
        var join = request.Join;

        var existing = await _state.TryGetPublisherAsync(join.DataTopic, join.FamilyWireName, join.InstanceId, cancellationToken);
        if (existing is not null && existing.WireNames.SetEquals(join.WireNames))
        {
            await _state.UpsertPublisherAsync(join.DataTopic, join, cancellationToken);
            _metrics.RecordJoinAdmission(join.DataTopic, join.FamilyWireName, go: true);
            await request.ReplyAsync(new NegotiationAck { Go = true }, cancellationToken);
            return;
        }

        var offenders = new List<NegotiationIncompatibility>();
        await foreach (var subscriber in _state.GetSubscribersAsync(join.DataTopic, join.FamilyWireName, cancellationToken))
        {
            if (!join.WireNames.Overlaps(subscriber.WireNames))
            {
                offenders.Add(new NegotiationIncompatibility
                {
                    InstanceId = subscriber.InstanceId,
                    FamilyWireName = join.FamilyWireName,
                    WireNames = subscriber.WireNames,
                });
            }
        }

        if (offenders.Count > 0)
        {
            _metrics.RecordJoinAdmission(join.DataTopic, join.FamilyWireName, go: false);
            await request.ReplyAsync(new NegotiationAck { Go = false, Offenders = offenders }, cancellationToken);
            return;
        }

        await _state.UpsertPublisherAsync(join.DataTopic, join, cancellationToken);
        _metrics.RecordJoinAdmission(join.DataTopic, join.FamilyWireName, go: true);
        await request.ReplyAsync(new NegotiationAck { Go = true }, cancellationToken);
    }
}
