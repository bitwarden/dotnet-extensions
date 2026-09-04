using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class ChainValidatorTests
{
    [Fact]
    public void ValidChain_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddPublisher<ValidChainPayload, ValidChainCeiling>("test");
    }

    [Fact]
    public void DuplicateVariant_Reported()
    {
        var ex = Assert.Throws<AggregateException>(() =>
            new ServiceCollection().AddPublisher<DuplicateVariantPayload, DuplicateVariantCeiling>("test"));

        AssertContainsMessage(ex, "more than once");
    }

    [Fact]
    public void ReferenceNotInVariants_Reported()
    {
        var ex = Assert.Throws<AggregateException>(() =>
            new ServiceCollection().AddPublisher<DanglingReferencePayload, DanglingReferenceCeiling>("test"));

        AssertContainsMessage(ex, "which is not listed");
    }

    [Fact]
    public void MultipleFloors_Reported()
    {
        var ex = Assert.Throws<AggregateException>(() =>
            new ServiceCollection().AddPublisher<MultipleFloorsPayload, MultipleFloorsCeiling>("test"));

        AssertContainsMessage(ex, "declares 2 floors");
    }

    [Fact]
    public void MultipleCeilings_Reported()
    {
        var ex = Assert.Throws<AggregateException>(() =>
            new ServiceCollection().AddPublisher<MultipleCeilingsPayload, MultipleCeilingsCeilingA>("test"));

        AssertContainsMessage(ex, "declares 2 ceilings");
    }

    [Fact]
    public void UnreachedVariant_Reported()
    {
        var ex = Assert.Throws<AggregateException>(() =>
            new ServiceCollection().AddPublisher<UnreachedVariantPayload, UnreachedVariantCeiling>("test"));

        AssertContainsMessage(ex, "remain unreached");
    }

    [Fact]
    public void BidirectionalInconsistency_Reported()
    {
        var ex = Assert.Throws<AggregateException>(() =>
            new ServiceCollection().AddPublisher<InconsistentPayload, InconsistentCeiling>("test"));

        AssertContainsMessage(ex, "Chain inconsistency");
    }

    [Fact]
    public void CycleInChain_Reported()
    {
        var ex = Assert.Throws<AggregateException>(() =>
            new ServiceCollection().AddPublisher<CyclePayload, CycleCeiling>("test"));

        AssertContainsMessage(ex, "cycle");
    }

    [Fact]
    public void DuplicateWireName_Reported()
    {
        var ex = Assert.Throws<AggregateException>(() =>
            new ServiceCollection().AddPublisher<DuplicateWireNamePayload, DuplicateWireNameCeiling>("test"));

        AssertContainsMessage(ex, "wire name");
    }

    [Fact]
    public void MultipleProblems_AllReported()
    {
        var ex = Assert.Throws<AggregateException>(() =>
            new ServiceCollection().AddPublisher<MultipleProblemsPayload, MultipleProblemsCeiling>("test"));

        Assert.True(ex.InnerExceptions.Count >= 2,
            $"Expected AggregateException with multiple inner exceptions; got {ex.InnerExceptions.Count}. " +
            $"Message: {ex.Message}");
    }

    private static void AssertContainsMessage(AggregateException ex, string fragment)
    {
        Assert.Contains(ex.InnerExceptions, e => e.Message.Contains(fragment));
    }
}

// ===== Valid chain =====
public sealed record ValidChainFloor : ValidChainPayload.IFloor<ValidChainMiddle>
{
    public ValidChainMiddle Upcast() => new();
}
public sealed record ValidChainMiddle : ValidChainPayload.IPure<ValidChainFloor, ValidChainCeiling>
{
    public ValidChainFloor Downcast() => new();
    public ValidChainCeiling Upcast() => new();
}
public sealed record ValidChainCeiling : ValidChainPayload.IPureCeiling<ValidChainMiddle>
{
    public ValidChainMiddle Downcast() => new();
}
public class ValidChainPayload : Payload<ValidChainPayload, ValidChainFloor, ValidChainCeiling>, IPayloadVariants<ValidChainPayload>
{
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(ValidChainFloor), nameof(ValidChainFloor)), (typeof(ValidChainMiddle), nameof(ValidChainMiddle)), (typeof(ValidChainCeiling), nameof(ValidChainCeiling))];
}

// ===== Duplicate variant in list =====
public sealed record DuplicateVariantFloor : DuplicateVariantPayload.IFloor<DuplicateVariantCeiling>
{
    public DuplicateVariantCeiling Upcast() => new();
}
public sealed record DuplicateVariantCeiling : DuplicateVariantPayload.IPureCeiling<DuplicateVariantFloor>
{
    public DuplicateVariantFloor Downcast() => new();
}
public class DuplicateVariantPayload : Payload<DuplicateVariantPayload, DuplicateVariantFloor, DuplicateVariantCeiling>, IPayloadVariants<DuplicateVariantPayload>
{
    // Same type listed twice.
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(DuplicateVariantFloor), nameof(DuplicateVariantFloor)), (typeof(DuplicateVariantCeiling), nameof(DuplicateVariantCeiling)), (typeof(DuplicateVariantCeiling), nameof(DuplicateVariantCeiling))];
}

// ===== TUp references a type not in Variants =====
public sealed record DanglingReferenceOrphan : DanglingReferencePayload.IVariant;
public sealed record DanglingReferenceFloor : DanglingReferencePayload.IFloor<DanglingReferenceOrphan>
{
    // TUp is Orphan, but Orphan is not listed in Variants.
    public DanglingReferenceOrphan Upcast() => new();
}
public sealed record DanglingReferenceCeiling : DanglingReferencePayload.IPureCeiling<DanglingReferenceFloor>
{
    public DanglingReferenceFloor Downcast() => new();
}
public class DanglingReferencePayload : Payload<DanglingReferencePayload, DanglingReferenceFloor, DanglingReferenceCeiling>, IPayloadVariants<DanglingReferencePayload>
{
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(DanglingReferenceFloor), nameof(DanglingReferenceFloor)), (typeof(DanglingReferenceCeiling), nameof(DanglingReferenceCeiling))];
}

// ===== Multiple floors (extra IFloor variant) =====
public sealed record MultipleFloorsFloorA : MultipleFloorsPayload.IFloor<MultipleFloorsCeiling>
{
    public MultipleFloorsCeiling Upcast() => new();
}
public sealed record MultipleFloorsFloorB : MultipleFloorsPayload.IFloor<MultipleFloorsCeiling>
{
    public MultipleFloorsCeiling Upcast() => new();
}
public sealed record MultipleFloorsCeiling : MultipleFloorsPayload.IPureCeiling<MultipleFloorsFloorA>
{
    public MultipleFloorsFloorA Downcast() => new();
}
public class MultipleFloorsPayload : Payload<MultipleFloorsPayload, MultipleFloorsFloorA, MultipleFloorsCeiling>, IPayloadVariants<MultipleFloorsPayload>
{
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(MultipleFloorsFloorA), nameof(MultipleFloorsFloorA)), (typeof(MultipleFloorsFloorB), nameof(MultipleFloorsFloorB)), (typeof(MultipleFloorsCeiling), nameof(MultipleFloorsCeiling))];
}

// ===== Multiple ceilings (extra IPureCeiling variant) =====
public sealed record MultipleCeilingsFloor : MultipleCeilingsPayload.IFloor<MultipleCeilingsCeilingA>
{
    public MultipleCeilingsCeilingA Upcast() => new();
}
public sealed record MultipleCeilingsCeilingA : MultipleCeilingsPayload.IPureCeiling<MultipleCeilingsFloor>
{
    public MultipleCeilingsFloor Downcast() => new();
}
public sealed record MultipleCeilingsCeilingB : MultipleCeilingsPayload.IPureCeiling<MultipleCeilingsFloor>
{
    public MultipleCeilingsFloor Downcast() => new();
}
public class MultipleCeilingsPayload : Payload<MultipleCeilingsPayload, MultipleCeilingsFloor, MultipleCeilingsCeilingA>, IPayloadVariants<MultipleCeilingsPayload>
{
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(MultipleCeilingsFloor), nameof(MultipleCeilingsFloor)), (typeof(MultipleCeilingsCeilingA), nameof(MultipleCeilingsCeilingA)), (typeof(MultipleCeilingsCeilingB), nameof(MultipleCeilingsCeilingB))];
}

// ===== Unreached variant: fork off the main chain (has TDown+TUp but not on the walk path) =====
public sealed record UnreachedVariantFloor : UnreachedVariantPayload.IFloor<UnreachedVariantMiddle>
{
    public UnreachedVariantMiddle Upcast() => new();
}
public sealed record UnreachedVariantMiddle : UnreachedVariantPayload.IPure<UnreachedVariantFloor, UnreachedVariantCeiling>
{
    public UnreachedVariantFloor Downcast() => new();
    public UnreachedVariantCeiling Upcast() => new();
}
public sealed record UnreachedVariantCeiling : UnreachedVariantPayload.IPureCeiling<UnreachedVariantMiddle>
{
    public UnreachedVariantMiddle Downcast() => new();
}
// This fork duplicates Middle's role but isn't reachable from Floor via TUp.
public sealed record UnreachedVariantFork : UnreachedVariantPayload.IPure<UnreachedVariantFloor, UnreachedVariantCeiling>
{
    public UnreachedVariantFloor Downcast() => new();
    public UnreachedVariantCeiling Upcast() => new();
}
public class UnreachedVariantPayload : Payload<UnreachedVariantPayload, UnreachedVariantFloor, UnreachedVariantCeiling>, IPayloadVariants<UnreachedVariantPayload>
{
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(UnreachedVariantFloor), nameof(UnreachedVariantFloor)), (typeof(UnreachedVariantMiddle), nameof(UnreachedVariantMiddle)), (typeof(UnreachedVariantCeiling), nameof(UnreachedVariantCeiling)), (typeof(UnreachedVariantFork), nameof(UnreachedVariantFork))];
}

// ===== Bidirectional inconsistency: middle's TDown doesn't match floor's identity =====
public sealed record InconsistentFloor : InconsistentPayload.IFloor<InconsistentMiddle>
{
    public InconsistentMiddle Upcast() => new();
}
// TDown points to Ceiling (nonsense), not Floor. Chain is inconsistent bidirectionally.
public sealed record InconsistentMiddle : InconsistentPayload.IPure<InconsistentCeiling, InconsistentCeiling>
{
    public InconsistentCeiling Downcast() => new();
    public InconsistentCeiling Upcast() => new();
}
public sealed record InconsistentCeiling : InconsistentPayload.IPureCeiling<InconsistentMiddle>
{
    public InconsistentMiddle Downcast() => new();
}
public class InconsistentPayload : Payload<InconsistentPayload, InconsistentFloor, InconsistentCeiling>, IPayloadVariants<InconsistentPayload>
{
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(InconsistentFloor), nameof(InconsistentFloor)), (typeof(InconsistentMiddle), nameof(InconsistentMiddle)), (typeof(InconsistentCeiling), nameof(InconsistentCeiling))];
}

// ===== Cycle: B.TUp points back at A, forming Floor -> A -> B -> A =====
public sealed record CycleFloor : CyclePayload.IFloor<CycleA>
{
    public CycleA Upcast() => new();
}
public sealed record CycleA : CyclePayload.IPure<CycleFloor, CycleB>
{
    public CycleFloor Downcast() => new();
    public CycleB Upcast() => new();
}
// TUp points back at A instead of forward to Ceiling — walk from floor cycles A -> B -> A.
public sealed record CycleB : CyclePayload.IPure<CycleA, CycleA>
{
    public CycleA Downcast() => new();
    public CycleA Upcast() => new();
}
public sealed record CycleCeiling : CyclePayload.IPureCeiling<CycleB>
{
    public CycleB Downcast() => new();
}
public class CyclePayload : Payload<CyclePayload, CycleFloor, CycleCeiling>, IPayloadVariants<CyclePayload>
{
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(CycleFloor), nameof(CycleFloor)), (typeof(CycleA), nameof(CycleA)), (typeof(CycleB), nameof(CycleB)), (typeof(CycleCeiling), nameof(CycleCeiling))];
}

// ===== Two variants share a wire name =====
public sealed record DuplicateWireNameFloor : DuplicateWireNamePayload.IFloor<DuplicateWireNameCeiling>
{
    public DuplicateWireNameCeiling Upcast() => new();
}
public sealed record DuplicateWireNameCeiling : DuplicateWireNamePayload.IPureCeiling<DuplicateWireNameFloor>
{
    public DuplicateWireNameFloor Downcast() => new();
}
public class DuplicateWireNamePayload : Payload<DuplicateWireNamePayload, DuplicateWireNameFloor, DuplicateWireNameCeiling>, IPayloadVariants<DuplicateWireNamePayload>
{
    // Both variants use the same wire name "shared" — dispatch would be ambiguous.
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(DuplicateWireNameFloor), "shared"), (typeof(DuplicateWireNameCeiling), "shared")];
}

// ===== Multiple problems: extra floor AND orphan reference — expect multiple errors =====
public sealed record MultipleProblemsFloorA : MultipleProblemsPayload.IFloor<MultipleProblemsCeiling>
{
    public MultipleProblemsCeiling Upcast() => new();
}
public sealed record MultipleProblemsFloorB : MultipleProblemsPayload.IFloor<MultipleProblemsUnlisted>
{
    // Points at an unlisted variant.
    public MultipleProblemsUnlisted Upcast() => new();
}
public sealed record MultipleProblemsUnlisted : MultipleProblemsPayload.IVariant;
public sealed record MultipleProblemsCeiling : MultipleProblemsPayload.IPureCeiling<MultipleProblemsFloorA>
{
    public MultipleProblemsFloorA Downcast() => new();
}
public class MultipleProblemsPayload : Payload<MultipleProblemsPayload, MultipleProblemsFloorA, MultipleProblemsCeiling>, IPayloadVariants<MultipleProblemsPayload>
{
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(MultipleProblemsFloorA), nameof(MultipleProblemsFloorA)), (typeof(MultipleProblemsFloorB), nameof(MultipleProblemsFloorB)), (typeof(MultipleProblemsCeiling), nameof(MultipleProblemsCeiling))];
}
