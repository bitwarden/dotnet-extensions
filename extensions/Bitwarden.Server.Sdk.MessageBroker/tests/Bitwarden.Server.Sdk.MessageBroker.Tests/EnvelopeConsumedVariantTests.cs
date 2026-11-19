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

public sealed class VersionedPayload : Payload<VersionedPayload, VersionedV1, VersionedV2>, IPayloadVariants<VersionedPayload>, IPayloadWireName<VersionedPayload>
{
    public static string WireName => "versioned";
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(VersionedV1), "v1"), (typeof(VersionedV2), "v2")];
}

// A V1 whose Upcast() throws when a field it expects is null — mirrors a wire message from an
// older publisher that lacks a field the current V1 record expects.
public sealed record ThrowingV1(string? RequiredField) : ThrowingPayload.IFloor<ThrowingV2>
{
    public ThrowingV2 Upcast() => new(RequiredField!.Trim());
}

public sealed record ThrowingV2(string Value) : ThrowingPayload.IPureCeiling<ThrowingV1>
{
    public ThrowingV1 Downcast() => new(Value);
}

public sealed class ThrowingPayload : Payload<ThrowingPayload, ThrowingV1, ThrowingV2>, IPayloadVariants<ThrowingPayload>, IPayloadWireName<ThrowingPayload>
{
    public static string WireName => "throwing";
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(ThrowingV1), "v1"), (typeof(ThrowingV2), "v2")];
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

    [Fact]
    public void Construction_WrapsThrowingUpcast_AsInvalidOperationException()
    {
        // Payload author's Upcast() runs on wire data, so a field deserialized as null can make
        // an ordinary .Trim() or Guid.Parse throw NullReferenceException. The envelope must
        // translate that into InvalidOperationException so the transports' dead-letter catches
        // hold instead of letting the exception wedge the consumer.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new TestEnvelope<ThrowingPayload, ThrowingV2>([new ThrowingV1(RequiredField: null)]));

        Assert.IsType<NullReferenceException>(ex.InnerException);
        Assert.Contains(nameof(ThrowingV1), ex.Message);
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
