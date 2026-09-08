namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

// A minimal V1→V2 chain for exercising the wire-name resolution on the envelope.
public sealed record VersionedV1 : VersionedPayload.IFloor<VersionedV2>
{
    public VersionedV2 Upcast() => new();
}

public sealed record VersionedV2 : VersionedPayload.IPureCeiling<VersionedV1>
{
    public VersionedV1 Downcast() => new();
}

public sealed class VersionedPayload : Payload<VersionedPayload, VersionedV1, VersionedV2>, IPayloadVariants<VersionedPayload>
{
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(VersionedV1), "v1"), (typeof(VersionedV2), "v2")];
}

/// <summary>
/// Verifies that the wire name exposed by <see cref="Envelope{TPayload, TCeiling}"/> reflects the
/// highest received variant at or below the ceiling — i.e. the variant the subscriber actually
/// deserialized from, not the ceiling it upcast to. This tag drives the consume-counter's
/// <c>messaging.variant.name</c> dimension, which is how operators see the constellation of variants
/// still in use across all subscribers.
/// </summary>
public class EnvelopeConsumedVariantTests
{
    [Fact]
    public void UsesCeilingWireName_WhenCeilingArrivesDirectly()
    {
        var envelope = new TestEnvelope<VersionedPayload, VersionedV2>(
            [new VersionedV2(), new VersionedV1()]);

        Assert.Equal("v2", envelope.ConsumedVariantWireName);
    }

    [Fact]
    public void UsesLowerVariantsWireName_WhenCeilingIsAbsentAndUpcastIsWalked()
    {
        // Simulates an older publisher that only sends V1: subscriber's TCeiling is V2, but the
        // wire never carried V2 (unknown-discriminator drop, or publisher predates V2). The
        // metric must report v1 — that's the variant this subscriber is really consuming, and
        // the signal operators use to decide V1 is still on the wire.
        var envelope = new TestEnvelope<VersionedPayload, VersionedV2>([new VersionedV1()]);

        Assert.Equal("v1", envelope.ConsumedVariantWireName);
        Assert.IsType<VersionedV2>(envelope.Payload);
    }

    private sealed class TestEnvelope<TPayload, TCeiling> : Envelope<TPayload, TCeiling>
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
    {
        public TestEnvelope(IEnumerable<Payload<TPayload>.IVariant> variants) : base(variants) { }

        public override string MessageId => "test";
        public override string? TraceId => null;
        public override int DeliveryCount => 1;

        protected override Task CompleteAsyncCore(CancellationToken cancellationToken) => Task.CompletedTask;
        protected override Task RequeueCoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        protected override Task DeadLetterAsyncCore(string? reason, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
