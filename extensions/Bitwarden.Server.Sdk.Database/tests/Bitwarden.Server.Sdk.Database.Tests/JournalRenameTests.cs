namespace Bitwarden.Server.Sdk.Database.Tests;

public class JournalRenameTests
{
    private const string Current = "Bit.Migrator.DbScripts";

    private static SqlServerMigrationOptions WithPrevious(string? previous)
        => new() { ScriptPrefix = Current, PreviousScriptPrefix = previous };

    [Fact]
    public void NoPreviousPrefix_IsTheDefault()
    {
        var options = new SqlServerMigrationOptions();

        Assert.Null(options.PreviousScriptPrefix);
        Assert.False(SqlServerMigrator.ShouldRenameJournalEntries(options));
    }

    [Fact]
    public void PreviousPrefix_RenamesForTheInitialPhase()
    {
        Assert.True(SqlServerMigrator.ShouldRenameJournalEntries(WithPrevious("Bit.Setup.DbScripts")));
    }

    [Fact]
    public void TransitionPhase_SkipsRenames()
    {
        // Nothing is recorded for an unjournaled phase, so there is no journal to rewrite.
        var options = WithPrevious("Bit.Setup.DbScripts");
        options.Phase = MigrationPhase.Transition;

        Assert.False(SqlServerMigrator.ShouldRenameJournalEntries(options));
    }

    [Fact]
    public void PrefixMatchingTheCurrentOne_IsNotRewritten()
    {
        // The journal already records what this build produces; rewriting would be a no-op UPDATE.
        Assert.False(SqlServerMigrator.ShouldRenameJournalEntries(WithPrevious(Current)));
        Assert.False(SqlServerMigrator.ShouldRenameJournalEntries(WithPrevious(Current + ".")));
    }

    [Fact]
    public void EmptyPrefixes_AreIgnored()
    {
        // An empty fragment would match every row and rewrite the whole journal.
        Assert.False(SqlServerMigrator.ShouldRenameJournalEntries(WithPrevious("")));
        Assert.False(SqlServerMigrator.ShouldRenameJournalEntries(
            new SqlServerMigrationOptions { PreviousScriptPrefix = "Bit.Setup.DbScripts" }));
    }

    [Fact]
    public void RenameStatement_ParameterisesTheValues()
    {
        var sql = SqlServerMigrator.JournalRenameSql;

        Assert.Contains("@from", sql);
        Assert.Contains("@to", sql);
        Assert.Contains("[dbo].[Migration]", sql);
        // Guards the table's existence, and touches only rows that actually contain the fragment.
        Assert.Contains("IF OBJECT_ID('dbo.Migration','U') IS NOT NULL", sql);
        Assert.Contains("WHERE CHARINDEX(@from, [ScriptName]) > 0", sql);
    }
}
