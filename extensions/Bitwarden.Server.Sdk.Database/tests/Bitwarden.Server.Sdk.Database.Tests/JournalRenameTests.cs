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
    public void OverlappingPrefixes_AreRejected()
    {
        // Either direction of overlap. The rewrite is only idempotent for unrelated names, so the
        // generator rejects these at build time (BWDB001) and hand-configured options throw rather
        // than silently rewriting nothing.
        Assert.Throws<InvalidOperationException>(
            () => SqlServerMigrator.ShouldRenameJournalEntries(WithPrevious("Bit.Migrator")));

        Assert.Throws<InvalidOperationException>(
            () => SqlServerMigrator.ShouldRenameJournalEntries(WithPrevious($"{Current}.SqlServer")));
    }

    [Fact]
    public void RenameStatement_ParameterisesTheValues()
    {
        var sql = SqlServerMigrator.JournalRenameSql;

        Assert.Contains("@from", sql);
        Assert.Contains("@to", sql);
        Assert.Contains("[dbo].[Migration]", sql);
        // Guards the table's existence.
        Assert.Contains("IF OBJECT_ID('dbo.Migration','U') IS NOT NULL", sql);
    }

    [Fact]
    public void RenameStatement_IsAnchoredAndRunnableTwice()
    {
        var sql = SqlServerMigrator.JournalRenameSql;

        // Matches the prefix only at the start of the name, and rewrites just that many characters.
        Assert.Contains("WHERE LEFT([ScriptName], LEN(@from)) = @from", sql);
        Assert.Contains("STUFF([ScriptName], 1, LEN(@from), @to)", sql);

        // No further guard is needed: overlapping prefixes are rejected before this runs, so a
        // rewritten name can never match the pattern that produced it.

        // An unanchored rewrite matched anywhere in the name and re-appended the new segment on
        // every run, mangling the journal until every applied script looked pending again.
        Assert.DoesNotContain("CHARINDEX", sql);
        Assert.DoesNotContain("REPLACE(", sql);
    }
}
