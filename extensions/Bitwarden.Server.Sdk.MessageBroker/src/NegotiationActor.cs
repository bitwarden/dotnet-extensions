using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Handles negotiation requests delivered by an <see cref="INegotiationTransport"/>, running
/// the admission code path shared by both message types: a subscriber's <see cref="Capability"/>
/// is admitted against every cached publisher for the same data-topic; a booting publisher's
/// <see cref="PublisherJoin"/> is admitted against every cached subscriber. In both cases
/// admission passes when the requester's wire-name set overlaps every counterparty's set;
/// otherwise a no-go reply lists the incompatible parties.
/// <para>
/// The actor relies on the transport to enforce single-active-consumer semantics per
/// data-topic: no explicit locking here. If two instances of this actor process the same
/// data-topic concurrently, the last cache write wins and admission decisions can race.
/// </para>
/// </summary>
internal sealed class NegotiationActor : BackgroundService
{
    private readonly INegotiationTransport _transport;
    private readonly INegotiationState _state;

    public NegotiationActor(INegotiationTransport transport, INegotiationState state)
    {
        _transport = transport;
        _state = state;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _transport.ReceiveRequestsAsync(stoppingToken))
        {
            switch (request)
            {
                case CapabilityRequest cap:
                    await HandleCapabilityAsync(cap, stoppingToken);
                    break;
                case JoinRequest join:
                    await HandleJoinAsync(join, stoppingToken);
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
