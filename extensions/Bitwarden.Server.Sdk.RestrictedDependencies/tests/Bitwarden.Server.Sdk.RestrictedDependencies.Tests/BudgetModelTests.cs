namespace Bitwarden.Server.Sdk.RestrictedDependencies.Tests;

public class BudgetModelTests
{
    [Fact]
    public void Serialize_SortsAndIsStableAcrossInputOrder()
    {
        var first = new BudgetModel("Bit.Core.Services.IUserService",
            ["M:B", "M:A"],
            [
                new BudgetEntry(DependencyUsageType.Member, "M:A", "Core", "M:Z.Run", 2),
                new BudgetEntry(DependencyUsageType.Injection, null, "Api", "M:Y.#ctor", 1),
                new BudgetEntry(DependencyUsageType.Injection, null, "Admin", "M:X.#ctor", 1),
            ]).Serialize();

        var second = new BudgetModel("Bit.Core.Services.IUserService",
            ["M:A", "M:B"],
            [
                new BudgetEntry(DependencyUsageType.Injection, null, "Admin", "M:X.#ctor", 1),
                new BudgetEntry(DependencyUsageType.Member, "M:A", "Core", "M:Z.Run", 2),
                new BudgetEntry(DependencyUsageType.Injection, null, "Api", "M:Y.#ctor", 1),
            ]).Serialize();

        Assert.Equal(first, second);
        Assert.Equal("""
            {
              "type": "Bit.Core.Services.IUserService",
              "declaredMembers": [
                "M:A",
                "M:B"
              ],
              "sites": [
                { "kind": "injection", "project": "Admin", "site": "M:X.#ctor" },
                { "kind": "injection", "project": "Api", "site": "M:Y.#ctor" },
                { "kind": "member", "member": "M:A", "project": "Core", "site": "M:Z.Run", "count": 2 }
              ]
            }

            """.Replace("\r\n", "\n"), first);
    }

    [Fact]
    public void Parse_RoundTripsSerialize()
    {
        var original = new BudgetModel("T",
            ["M:A"],
            [
                new BudgetEntry(DependencyUsageType.Escape, null, "Identity", "P:X._userService", 1),
                new BudgetEntry(DependencyUsageType.Injection, null, "Api", "M:Y.#ctor(T)", 2),
                new BudgetEntry(DependencyUsageType.Member, "M:A", "Core", "M:Z.Run", 1),
            ]);

        var parsed = BudgetModel.Parse(original.Serialize());

        Assert.Equal(original.Serialize(), parsed.Serialize());
        Assert.Equal(2, parsed.Usages.Single(s => s.Kind == DependencyUsageType.Injection).Count);
    }

    [Fact]
    public void Serialize_EmptyCollections_WriteEmptyArrays()
    {
        var text = new BudgetModel("T", [], []).Serialize();

        Assert.Equal("{\n  \"type\": \"T\",\n  \"declaredMembers\": [],\n  \"sites\": []\n}\n", text);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{ \"declaredMembers\": [], \"sites\": [] }")]
    [InlineData("{ \"type\": \"T\", \"declaredMembers\": [], \"sites\": [ { \"kind\": \"bogus\", \"project\": \"P\", \"site\": \"S\" } ] }")]
    [InlineData("{ \"type\": \"T\", \"declaredMembers\": [], \"sites\": [ { \"kind\": \"member\", \"project\": \"P\", \"site\": \"S\" } ] }")]
    [InlineData("{ \"type\": \"T\", \"declaredMembers\": [], \"sites\": [ { \"kind\": \"injection\", \"project\": \"P\", \"site\": \"S\", \"count\": 0 } ] }")]
    [InlineData("{ \"type\": \"T\", \"declaredMembers\": [], \"sites\": [ { \"kind\": \"injection\", \"project\": \"P\", \"site\": \"S\", \"count\": 1.5 } ] }")]
    [InlineData("{ \"type\": \"T\", \"declaredMembers\": [], \"sites\": [ 1 ] }")]
    [InlineData("{ \"type\": \"T\", \"declaredMembers\": [], \"sites\": [ { \"kind\": \"injection\", \"project\": \"P\", \"site\": \"S\", \"tracked\": \"yes\" } ] }")]
    // Above int.MaxValue: must still be a FormatException, since that is the only exception both
    // callers catch. It used to overflow and escape them.
    [InlineData("{ \"type\": \"T\", \"declaredMembers\": [], \"sites\": [ { \"kind\": \"injection\", \"project\": \"P\", \"site\": \"S\", \"count\": 3000000000 } ] }")]
    public void Parse_RejectsMalformedDocuments(string json)
    {
        Assert.Throws<FormatException>(() => BudgetModel.Parse(json));
    }

    [Fact]
    public void Parse_RejectsDuplicateSiteRows()
    {
        // The analyzer sums matching rows into one budget but checks staleness a row at a time, so
        // two rows of count 2 for the same key would permit four uses while three uses still
        // satisfied each row - budget nothing would ever report as stale.
        var error = Assert.Throws<FormatException>(() => BudgetModel.Parse("""
            {
              "type": "T",
              "declaredMembers": [],
              "sites": [
                { "kind": "member", "member": "M:A", "project": "Core", "site": "M:Z.Run", "count": 2 },
                { "kind": "member", "member": "M:A", "project": "Core", "site": "M:Z.Run", "count": 2 }
              ]
            }
            """));

        Assert.Contains("Duplicate 'member' site 'M:Z.Run'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_AcceptsTwoRestrictedMembersUsedAtOneSite()
    {
        // Only Kind, Member, Project and Site together make a row a duplicate, because that is the
        // key the analyzer budgets by. One method using two restricted members is two rows.
        var parsed = BudgetModel.Parse("""
            {
              "type": "T",
              "declaredMembers": [],
              "sites": [
                { "kind": "member", "member": "M:A", "project": "Core", "site": "M:Z.Run", "count": 1 },
                { "kind": "member", "member": "M:B", "project": "Core", "site": "M:Z.Run", "count": 1 }
              ]
            }
            """);

        Assert.Equal(["M:A", "M:B"], parsed.Usages.Select(usage => usage.Member));
    }

    [Fact]
    public void Parse_EscapesInSiteKeysSurvive()
    {
        var site = "M:Bit.Api.Controller.#ctor(System.Collections.Generic.Dictionary{System.String,System.String})";
        var text = new BudgetModel("T", [], [new BudgetEntry(DependencyUsageType.Injection, null, "Api", site, 1)]).Serialize();

        Assert.Equal(site, BudgetModel.Parse(text).Usages.Single().Site);
    }

    /// <summary>
    /// <c>tracked</c> is written only when true, so a document with no tracked-only rows is
    /// byte-identical to one written before the field existed.
    /// </summary>
    [Fact]
    public void Serialize_WritesTrackedOnlyWhenTrue_AndParseRoundTripsIt()
    {
        var text = new BudgetModel("T",
            [],
            [
                new BudgetEntry(DependencyUsageType.Member, "M:A", "Core", "M:Z.Run", 2, Tracked: true),
                new BudgetEntry(DependencyUsageType.Injection, null, "Api", "M:Y.#ctor", 1),
            ]).Serialize();

        Assert.Equal("""
            {
              "type": "T",
              "declaredMembers": [],
              "sites": [
                { "kind": "injection", "project": "Api", "site": "M:Y.#ctor" },
                { "kind": "member", "member": "M:A", "project": "Core", "site": "M:Z.Run", "count": 2, "tracked": true }
              ]
            }

            """.Replace("\r\n", "\n"), text);

        var parsed = BudgetModel.Parse(text);
        Assert.Equal(text, parsed.Serialize());
        Assert.True(parsed.Usages.Single(s => s.Kind == DependencyUsageType.Member).Tracked);
        Assert.False(parsed.Usages.Single(s => s.Kind == DependencyUsageType.Injection).Tracked);
    }
}
