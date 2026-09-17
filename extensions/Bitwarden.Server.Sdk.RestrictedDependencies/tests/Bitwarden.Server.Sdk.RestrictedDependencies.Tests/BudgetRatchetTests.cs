namespace Bitwarden.Server.Sdk.RestrictedDependencies.Tests;

public class BudgetRatchetTests
{
    private static BudgetModel Budget(params BudgetEntry[] sites) => new("T", ["M:A"], [.. sites]);

    [Fact]
    public void FindGrowth_NetZeroMoveAcrossProjectsAndMethods_ReturnsEmpty()
    {
        var before = Budget(
            new BudgetEntry(DependencyUsageType.Member, "M:A", "Api", "M:X.Run", 2),
            new BudgetEntry(DependencyUsageType.Injection, null, "Api", "M:X.#ctor", 1));
        var after = Budget(
            new BudgetEntry(DependencyUsageType.Member, "M:A", "Core", "M:Y.Run", 1),
            new BudgetEntry(DependencyUsageType.Member, "M:A", "Core", "M:Z.Run", 1),
            new BudgetEntry(DependencyUsageType.Injection, null, "Core", "M:Y.#ctor", 1));

        Assert.Empty(BudgetRatchet.FindGrowth([before], [after]));
    }

    [Fact]
    public void FindGrowth_CountIncrease_ReportsTheRisingTotal()
    {
        var before = Budget(new BudgetEntry(DependencyUsageType.Member, "M:A", "Api", "M:X.Run", 2));
        var after = Budget(new BudgetEntry(DependencyUsageType.Member, "M:A", "Api", "M:X.Run", 3));

        var growth = BudgetRatchet.FindGrowth([before], [after]);

        Assert.Equal(new[] { "T: member 'M:A' total rose from 2 to 3." }, growth.ToArray());
    }

    [Fact]
    public void FindGrowth_NewKindAtNewSite_ReportsTheRisingTotal()
    {
        var before = Budget();
        var after = Budget(new BudgetEntry(DependencyUsageType.Locator, null, "Core", "M:X.Resolve", 1));

        Assert.Equal(new[] { "T: locator total rose from 0 to 1." }, BudgetRatchet.FindGrowth([before], [after]).ToArray());
    }

    [Fact]
    public void FindGrowth_NewDeclaredMember_ReportsTheGainedMember()
    {
        var before = Budget();
        var after = new BudgetModel("T", ["M:A", "M:B"], []);

        Assert.Equal(new[] { "T: sealed type gained member 'M:B'." }, BudgetRatchet.FindGrowth([before], [after]).ToArray());
    }

    [Fact]
    public void FindGrowth_RemovedDeclaredMemberAndShrunkCount_ReturnsEmpty()
    {
        var before = new BudgetModel("T", ["M:A", "M:B"], [new BudgetEntry(DependencyUsageType.Injection, null, "Api", "M:X.#ctor", 3)]);
        var after = new BudgetModel("T", ["M:A"], [new BudgetEntry(DependencyUsageType.Injection, null, "Api", "M:X.#ctor", 1)]);

        Assert.Empty(BudgetRatchet.FindGrowth([before], [after]));
    }

    [Fact]
    public void FindGrowth_NewBaseline_ReportsEveryMemberAndTotal()
    {
        var after = Budget(new BudgetEntry(DependencyUsageType.Injection, null, "Api", "M:X.#ctor", 1));

        Assert.Equal(
            new[] { "T: sealed type gained member 'M:A'.", "T: injection total rose from 0 to 1." },
            BudgetRatchet.FindGrowth([], [after]).ToArray());
    }

    /// <summary>
    /// A tracked-only rule allows new uses by design and the analyzer never reports one, so the
    /// row it writes is expected to rise. Comparing it would fail the check for exactly the code
    /// the rule permits.
    /// </summary>
    [Fact]
    public void FindGrowth_TrackedRowRising_ReturnsEmpty()
    {
        var before = Budget(new BudgetEntry(DependencyUsageType.Member, "M:A", "Api", "M:X.Run", 2, Tracked: true));
        var after = Budget(new BudgetEntry(DependencyUsageType.Member, "M:A", "Api", "M:X.Run", 5, Tracked: true));

        Assert.Empty(BudgetRatchet.FindGrowth([before], [after]));
    }

    [Fact]
    public void FindGrowth_GatedRowRisingBesideATrackedOne_ReportsOnlyTheGatedTotal()
    {
        var before = Budget(
            new BudgetEntry(DependencyUsageType.Member, "M:A", "Api", "M:X.Run", 1, Tracked: true),
            new BudgetEntry(DependencyUsageType.Injection, null, "Api", "M:X.#ctor", 1));
        var after = Budget(
            new BudgetEntry(DependencyUsageType.Member, "M:A", "Api", "M:X.Run", 4, Tracked: true),
            new BudgetEntry(DependencyUsageType.Injection, null, "Api", "M:X.#ctor", 2));

        Assert.Equal(
            new[] { "T: injection total rose from 1 to 2." },
            BudgetRatchet.FindGrowth([before], [after]).ToArray());
    }
}
