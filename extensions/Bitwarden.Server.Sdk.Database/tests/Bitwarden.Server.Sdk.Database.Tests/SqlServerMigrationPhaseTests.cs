namespace Bitwarden.Server.Sdk.Database.Tests;

public class SqlServerMigrationPhaseTests
{
    private const string Prefix = "Bit.OrderDatabase.SqlServer";
    private const string Root = $"{Prefix}.2026-08-12_00_CreateOrders.sql";
    private const string Transition = $"{Prefix}.Transition.2026-08-19_00_Backfill.sql";
    private const string Archived = $"{Prefix}.Archive.2020-01-01_00_Old.sql";

    private static Func<string, bool> Filter(MigrationPhase phase, string prefix = Prefix)
        => SqlServerMigrator.ScriptFilterFor(phase, prefix);

    [Fact]
    public void InitialPhase_TakesScriptsDirectlyUnderTheFolder()
    {
        Assert.True(Filter(MigrationPhase.Initial)(Root));
    }

    [Fact]
    public void InitialPhase_ExcludesTheTransitionFolder()
    {
        // A transition script's name contains the root segment as a prefix, so without an explicit
        // exclusion the initial phase would apply both sets.
        Assert.False(Filter(MigrationPhase.Initial)(Transition));
    }

    [Fact]
    public void TransitionPhase_TakesOnlyTheTransitionFolder()
    {
        var filter = Filter(MigrationPhase.Transition);

        Assert.True(filter(Transition));
        Assert.False(filter(Root));
    }

    [Fact]
    public void InitialPhase_ExcludesArchivedScripts()
    {
        Assert.False(Filter(MigrationPhase.Initial)(Archived));
    }

    [Fact]
    public void AnotherSchemasScripts_AreNeverMatched()
    {
        // Prefix matching is anchored, so a schema sharing the assembly can't leak into this one.
        Assert.False(Filter(MigrationPhase.Initial)("Bit.OtherDatabase.SqlServer.2026-08-12_00_X.sql"));
    }

    [Fact]
    public void InheritedPrefix_NestsTheSameWay()
    {
        // The names an inherited journal holds: initial unchanged, transition nested inside.
        var initial = Filter(MigrationPhase.Initial, "Bit.Migrator.DbScripts");
        var transition = Filter(MigrationPhase.Transition, "Bit.Migrator.DbScripts");

        Assert.True(initial("Bit.Migrator.DbScripts.2026-08-01_00_Procedures.sql"));
        Assert.False(initial("Bit.Migrator.DbScripts.Transition.2026-08-19_00_Backfill.sql"));
        Assert.True(transition("Bit.Migrator.DbScripts.Transition.2026-08-19_00_Backfill.sql"));
    }

    [Fact]
    public void MissingPrefix_FailsLoudly()
    {
        // Silently matching nothing would report a successful migration that applied no scripts.
        var error = Assert.Throws<InvalidOperationException>(
            () => SqlServerMigrator.ScriptFilterFor(MigrationPhase.Initial, ""));

        Assert.Contains("BitSqlServerScriptPrefix", error.Message);
    }

    [Fact]
    public void OnlyTheInitialPhaseIsJournaled()
    {
        // A journaled transition script would apply once and never again, which defeats the point
        // of a phase meant to be re-runnable while the window is open.
        Assert.True(SqlServerMigrator.IsJournaled(MigrationPhase.Initial));
        Assert.False(SqlServerMigrator.IsJournaled(MigrationPhase.Transition));
    }

    [Fact]
    public void DefaultOptions_AreTheInitialPhaseAgainstTheDefaultFolder()
    {
        var options = new SqlServerMigrationOptions();

        Assert.Equal(MigrationPhase.Initial, options.Phase);
        Assert.Equal("", options.ScriptPrefix);
        Assert.False(options.NoTransaction);
    }
}
