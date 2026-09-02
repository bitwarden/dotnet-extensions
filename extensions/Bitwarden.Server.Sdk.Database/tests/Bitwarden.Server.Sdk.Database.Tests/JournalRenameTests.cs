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
    public void PrefixThatTheCurrentOneExtends_IsStillRewritten()
    {
        // Moving scripts into a provider subfolder under the same root. The statement is anchored
        // and skips rows already carrying the new prefix, so this shape is safe rather than banned.
        Assert.True(SqlServerMigrator.ShouldRenameJournalEntries(
            WithPrevious("Bit.Migrator")));
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

        // Rows already carrying the new prefix are left alone, which is what makes a second run a
        // no-op when the new prefix extends the old one.
        Assert.Contains("AND LEFT([ScriptName], LEN(@to)) <> @to", sql);

        // An unanchored rewrite matched anywhere in the name and re-appended the new segment on
        // every run, mangling the journal until every applied script looked pending again.
        Assert.DoesNotContain("CHARINDEX", sql);
        Assert.DoesNotContain("REPLACE(", sql);
    }
}
