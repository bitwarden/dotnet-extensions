using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class ChainValidatorTests
{
    [Fact]
    public void ValidChain_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddPublisher<ValidChainFamily, ValidChainCeiling>("test");
    }

    [Fact]
    public void DuplicateVariant_Reported()
    {
        var ex = Assert.Throws<AggregateException>(() =>
            new ServiceCollection().AddPublisher<DuplicateVariantFamily, DuplicateVariantCeiling>("test"));

        AssertContainsMessage(ex, "more than once");
    }

    [Fact]
    public void ReferenceNotInVariants_Reported()
    {
        var ex = Assert.Throws<AggregateException>(() =>
            new ServiceCollection().AddPublisher<DanglingReferenceFamily, DanglingReferenceCeiling>("test"));

        AssertContainsMessage(ex, "which is not listed");
    }

    [Fact]
    public void MultipleFloors_Reported()
    {
        var ex = Assert.Throws<AggregateException>(() =>
            new ServiceCollection().AddPublisher<MultipleFloorsFamily, MultipleFloorsCeiling>("test"));

        AssertContainsMessage(ex, "declares 2 floors");
    }

    [Fact]
    public void MultipleCeilings_Reported()
    {
        var ex = Assert.Throws<AggregateException>(() =>
            new ServiceCollection().AddPublisher<MultipleCeilingsFamily, MultipleCeilingsCeilingA>("test"));

        AssertContainsMessage(ex, "declares 2 ceilings");
    }

    [Fact]
    public void UnreachedVariant_Reported()
    {
        var ex = Assert.Throws<AggregateException>(() =>
            new ServiceCollection().AddPublisher<UnreachedVariantFamily, UnreachedVariantCeiling>("test"));

        AssertContainsMessage(ex, "remain unreached");
    }

    [Fact]
    public void BidirectionalInconsistency_Reported()
    {
        var ex = Assert.Throws<AggregateException>(() =>
            new ServiceCollection().AddPublisher<InconsistentFamily, InconsistentCeiling>("test"));

        AssertContainsMessage(ex, "Chain inconsistency");
    }

    [Fact]
    public void CycleInChain_Reported()
    {
        var ex = Assert.Throws<AggregateException>(() =>
            new ServiceCollection().AddPublisher<CycleFamily, CycleCeiling>("test"));

        AssertContainsMessage(ex, "cycle");
    }

    [Fact]
    public void DuplicateWireName_Reported()
    {
        var ex = Assert.Throws<AggregateException>(() =>
            new ServiceCollection().AddPublisher<DuplicateWireNameFamily, DuplicateWireNameCeiling>("test"));

        AssertContainsMessage(ex, "wire name");
    }

    [Fact]
    public void MultipleProblems_AllReported()
    {
        var ex = Assert.Throws<AggregateException>(() =>
            new ServiceCollection().AddPublisher<MultipleProblemsFamily, MultipleProblemsCeiling>("test"));

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
public sealed record ValidChainFloor : ValidChainFamily.IFloor<ValidChainMiddle>
{
    public ValidChainMiddle Upcast() => new();
}
public sealed record ValidChainMiddle : ValidChainFamily.IPure<ValidChainFloor, ValidChainCeiling>
{
    public ValidChainFloor Downcast() => new();
    public ValidChainCeiling Upcast() => new();
}
public sealed record ValidChainCeiling : ValidChainFamily.IPureCeiling<ValidChainMiddle>
{
    public ValidChainMiddle Downcast() => new();
}
public class ValidChainFamily : Family<ValidChainFamily, ValidChainFloor, ValidChainCeiling>, IFamilyVariants<ValidChainFamily>, IFamilyWireName<ValidChainFamily>
{
    public static string WireName => "valid-chain";
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(ValidChainFloor), nameof(ValidChainFloor)), (typeof(ValidChainMiddle), nameof(ValidChainMiddle)), (typeof(ValidChainCeiling), nameof(ValidChainCeiling))];
}

// ===== Duplicate variant in list =====
public sealed record DuplicateVariantFloor : DuplicateVariantFamily.IFloor<DuplicateVariantCeiling>
{
    public DuplicateVariantCeiling Upcast() => new();
}
public sealed record DuplicateVariantCeiling : DuplicateVariantFamily.IPureCeiling<DuplicateVariantFloor>
{
    public DuplicateVariantFloor Downcast() => new();
}
public class DuplicateVariantFamily : Family<DuplicateVariantFamily, DuplicateVariantFloor, DuplicateVariantCeiling>, IFamilyVariants<DuplicateVariantFamily>, IFamilyWireName<DuplicateVariantFamily>
{
    public static string WireName => "duplicate-variant";
    // Same type listed twice.
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(DuplicateVariantFloor), nameof(DuplicateVariantFloor)), (typeof(DuplicateVariantCeiling), nameof(DuplicateVariantCeiling)), (typeof(DuplicateVariantCeiling), nameof(DuplicateVariantCeiling))];
}

// ===== TUp references a type not in Variants =====
public sealed record DanglingReferenceOrphan : DanglingReferenceFamily.IVariant;
public sealed record DanglingReferenceFloor : DanglingReferenceFamily.IFloor<DanglingReferenceOrphan>
{
    // TUp is Orphan, but Orphan is not listed in Variants.
    public DanglingReferenceOrphan Upcast() => new();
}
public sealed record DanglingReferenceCeiling : DanglingReferenceFamily.IPureCeiling<DanglingReferenceFloor>
{
    public DanglingReferenceFloor Downcast() => new();
}
public class DanglingReferenceFamily : Family<DanglingReferenceFamily, DanglingReferenceFloor, DanglingReferenceCeiling>, IFamilyVariants<DanglingReferenceFamily>, IFamilyWireName<DanglingReferenceFamily>
{
    public static string WireName => "dangling-reference";
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(DanglingReferenceFloor), nameof(DanglingReferenceFloor)), (typeof(DanglingReferenceCeiling), nameof(DanglingReferenceCeiling))];
}

// ===== Multiple floors (extra IFloor variant) =====
public sealed record MultipleFloorsFloorA : MultipleFloorsFamily.IFloor<MultipleFloorsCeiling>
{
    public MultipleFloorsCeiling Upcast() => new();
}
public sealed record MultipleFloorsFloorB : MultipleFloorsFamily.IFloor<MultipleFloorsCeiling>
{
    public MultipleFloorsCeiling Upcast() => new();
}
public sealed record MultipleFloorsCeiling : MultipleFloorsFamily.IPureCeiling<MultipleFloorsFloorA>
{
    public MultipleFloorsFloorA Downcast() => new();
}
public class MultipleFloorsFamily : Family<MultipleFloorsFamily, MultipleFloorsFloorA, MultipleFloorsCeiling>, IFamilyVariants<MultipleFloorsFamily>, IFamilyWireName<MultipleFloorsFamily>
{
    public static string WireName => "multiple-floors";
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(MultipleFloorsFloorA), nameof(MultipleFloorsFloorA)), (typeof(MultipleFloorsFloorB), nameof(MultipleFloorsFloorB)), (typeof(MultipleFloorsCeiling), nameof(MultipleFloorsCeiling))];
}

// ===== Multiple ceilings (extra IPureCeiling variant) =====
public sealed record MultipleCeilingsFloor : MultipleCeilingsFamily.IFloor<MultipleCeilingsCeilingA>
{
    public MultipleCeilingsCeilingA Upcast() => new();
}
public sealed record MultipleCeilingsCeilingA : MultipleCeilingsFamily.IPureCeiling<MultipleCeilingsFloor>
{
    public MultipleCeilingsFloor Downcast() => new();
}
public sealed record MultipleCeilingsCeilingB : MultipleCeilingsFamily.IPureCeiling<MultipleCeilingsFloor>
{
    public MultipleCeilingsFloor Downcast() => new();
}
public class MultipleCeilingsFamily : Family<MultipleCeilingsFamily, MultipleCeilingsFloor, MultipleCeilingsCeilingA>, IFamilyVariants<MultipleCeilingsFamily>, IFamilyWireName<MultipleCeilingsFamily>
{
    public static string WireName => "multiple-ceilings";
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(MultipleCeilingsFloor), nameof(MultipleCeilingsFloor)), (typeof(MultipleCeilingsCeilingA), nameof(MultipleCeilingsCeilingA)), (typeof(MultipleCeilingsCeilingB), nameof(MultipleCeilingsCeilingB))];
}

// ===== Unreached variant: fork off the main chain (has TDown+TUp but not on the walk path) =====
public sealed record UnreachedVariantFloor : UnreachedVariantFamily.IFloor<UnreachedVariantMiddle>
{
    public UnreachedVariantMiddle Upcast() => new();
}
public sealed record UnreachedVariantMiddle : UnreachedVariantFamily.IPure<UnreachedVariantFloor, UnreachedVariantCeiling>
{
    public UnreachedVariantFloor Downcast() => new();
    public UnreachedVariantCeiling Upcast() => new();
}
public sealed record UnreachedVariantCeiling : UnreachedVariantFamily.IPureCeiling<UnreachedVariantMiddle>
{
    public UnreachedVariantMiddle Downcast() => new();
}
// This fork duplicates Middle's role but isn't reachable from Floor via TUp.
public sealed record UnreachedVariantFork : UnreachedVariantFamily.IPure<UnreachedVariantFloor, UnreachedVariantCeiling>
{
    public UnreachedVariantFloor Downcast() => new();
    public UnreachedVariantCeiling Upcast() => new();
}
public class UnreachedVariantFamily : Family<UnreachedVariantFamily, UnreachedVariantFloor, UnreachedVariantCeiling>, IFamilyVariants<UnreachedVariantFamily>, IFamilyWireName<UnreachedVariantFamily>
{
    public static string WireName => "unreached-variant";
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(UnreachedVariantFloor), nameof(UnreachedVariantFloor)), (typeof(UnreachedVariantMiddle), nameof(UnreachedVariantMiddle)), (typeof(UnreachedVariantCeiling), nameof(UnreachedVariantCeiling)), (typeof(UnreachedVariantFork), nameof(UnreachedVariantFork))];
}

// ===== Bidirectional inconsistency: middle's TDown doesn't match floor's identity =====
public sealed record InconsistentFloor : InconsistentFamily.IFloor<InconsistentMiddle>
{
    public InconsistentMiddle Upcast() => new();
}
// TDown points to Ceiling (nonsense), not Floor. Chain is inconsistent bidirectionally.
public sealed record InconsistentMiddle : InconsistentFamily.IPure<InconsistentCeiling, InconsistentCeiling>
{
    public InconsistentCeiling Downcast() => new();
    public InconsistentCeiling Upcast() => new();
}
public sealed record InconsistentCeiling : InconsistentFamily.IPureCeiling<InconsistentMiddle>
{
    public InconsistentMiddle Downcast() => new();
}
public class InconsistentFamily : Family<InconsistentFamily, InconsistentFloor, InconsistentCeiling>, IFamilyVariants<InconsistentFamily>, IFamilyWireName<InconsistentFamily>
{
    public static string WireName => "inconsistent";
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(InconsistentFloor), nameof(InconsistentFloor)), (typeof(InconsistentMiddle), nameof(InconsistentMiddle)), (typeof(InconsistentCeiling), nameof(InconsistentCeiling))];
}

// ===== Cycle: B.TUp points back at A, forming Floor -> A -> B -> A =====
public sealed record CycleFloor : CycleFamily.IFloor<CycleA>
{
    public CycleA Upcast() => new();
}
public sealed record CycleA : CycleFamily.IPure<CycleFloor, CycleB>
{
    public CycleFloor Downcast() => new();
    public CycleB Upcast() => new();
}
// TUp points back at A instead of forward to Ceiling — walk from floor cycles A -> B -> A.
public sealed record CycleB : CycleFamily.IPure<CycleA, CycleA>
{
    public CycleA Downcast() => new();
    public CycleA Upcast() => new();
}
public sealed record CycleCeiling : CycleFamily.IPureCeiling<CycleB>
{
    public CycleB Downcast() => new();
}
public class CycleFamily : Family<CycleFamily, CycleFloor, CycleCeiling>, IFamilyVariants<CycleFamily>, IFamilyWireName<CycleFamily>
{
    public static string WireName => "cycle";
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(CycleFloor), nameof(CycleFloor)), (typeof(CycleA), nameof(CycleA)), (typeof(CycleB), nameof(CycleB)), (typeof(CycleCeiling), nameof(CycleCeiling))];
}

// ===== Two variants share a wire name =====
public sealed record DuplicateWireNameFloor : DuplicateWireNameFamily.IFloor<DuplicateWireNameCeiling>
{
    public DuplicateWireNameCeiling Upcast() => new();
}
public sealed record DuplicateWireNameCeiling : DuplicateWireNameFamily.IPureCeiling<DuplicateWireNameFloor>
{
    public DuplicateWireNameFloor Downcast() => new();
}
public class DuplicateWireNameFamily : Family<DuplicateWireNameFamily, DuplicateWireNameFloor, DuplicateWireNameCeiling>, IFamilyVariants<DuplicateWireNameFamily>, IFamilyWireName<DuplicateWireNameFamily>
{
    public static string WireName => "duplicate-wire-name";
    // Both variants use the same wire name "shared" — dispatch would be ambiguous.
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(DuplicateWireNameFloor), "shared"), (typeof(DuplicateWireNameCeiling), "shared")];
}

// ===== Multiple problems: extra floor AND orphan reference — expect multiple errors =====
public sealed record MultipleProblemsFloorA : MultipleProblemsFamily.IFloor<MultipleProblemsCeiling>
{
    public MultipleProblemsCeiling Upcast() => new();
}
public sealed record MultipleProblemsFloorB : MultipleProblemsFamily.IFloor<MultipleProblemsUnlisted>
{
    // Points at an unlisted variant.
    public MultipleProblemsUnlisted Upcast() => new();
}
public sealed record MultipleProblemsUnlisted : MultipleProblemsFamily.IVariant;
public sealed record MultipleProblemsCeiling : MultipleProblemsFamily.IPureCeiling<MultipleProblemsFloorA>
{
    public MultipleProblemsFloorA Downcast() => new();
}
public class MultipleProblemsFamily : Family<MultipleProblemsFamily, MultipleProblemsFloorA, MultipleProblemsCeiling>, IFamilyVariants<MultipleProblemsFamily>, IFamilyWireName<MultipleProblemsFamily>
{
    public static string WireName => "multiple-problems";
    public static IReadOnlyList<(Type, string)> Variants =>
        [(typeof(MultipleProblemsFloorA), nameof(MultipleProblemsFloorA)), (typeof(MultipleProblemsFloorB), nameof(MultipleProblemsFloorB)), (typeof(MultipleProblemsCeiling), nameof(MultipleProblemsCeiling))];
}
