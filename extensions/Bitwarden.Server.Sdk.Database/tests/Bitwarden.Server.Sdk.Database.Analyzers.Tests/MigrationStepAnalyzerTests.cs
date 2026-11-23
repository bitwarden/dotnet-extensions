using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Bitwarden.Server.Sdk.Database.Analyzers.Tests;

public partial class MigrationStepAnalyzerTests
{
    [Theory]
    [ClassData(typeof(BreakingSteps))]
    public async Task BreakingStep_IsReportedWithItsReason(string description, string step, string reason)
        => await RunAsync(
            Up(step, description),
            new DiagnosticResult("BW0026", DiagnosticSeverity.Warning).WithLocation(0).WithArguments(StepName(step), reason));

    [Theory]
    [ClassData(typeof(ConstraintSteps))]
    public async Task ConstraintOnExistingTable_IsReportedWithWhatItAdds(string description, string step, string constraint)
        => await RunAsync(
            Up(step, description),
            new DiagnosticResult("BW0027", DiagnosticSeverity.Warning).WithLocation(0).WithArguments(StepName(step), constraint, "'Items'"));

    [Theory]
    [ClassData(typeof(HarmlessSteps))]
    public async Task HarmlessStep_IsNotReported(string description, string step)
        => await RunAsync(Up(step, description));

    [Fact]
    public async Task BreakingStepMessage_SaysWhatBreaksAndWhatToDo()
        => await RunAsync(
            Up("""migrationBuilder.{|#0:DropColumn|}(name: "Legacy", table: "Items");"""),
            new DiagnosticResult("BW0026", DiagnosticSeverity.Warning).WithLocation(0).WithMessage(
                "DropColumn breaks the previous release: it drops a column the previous release may still read or write. "
                + "The previous release still runs against this schema until this release finishes rolling out. "
                + "Make the change across releases instead, or suppress this warning with a justification saying why "
                + "the previous release is unaffected."));

    [Fact]
    public async Task ConstraintMessage_SaysWhatFailsAndWhatToDo()
        => await RunAsync(
            Up("""migrationBuilder.{|#0:CreateIndex|}(name: "IX_Items_Name", table: "Items", column: "Name", unique: true);"""),
            new DiagnosticResult("BW0027", DiagnosticSeverity.Warning).WithLocation(0).WithMessage(
                "CreateIndex adds a unique index to 'Items'. The migration fails if rows already there don't satisfy it, "
                + "and the previous release's writes fail if they don't satisfy it while this release rolls out. "
                + "Confirm that both satisfy it, then suppress this warning with a justification saying why."));

    [Fact]
    public async Task ConstraintMessage_OnATableItCantName_SaysTheTable()
        => await RunAsync(
            Up("""
                string table = "Items";
                migrationBuilder.{|#0:AddCheckConstraint|}(name: "CK_Count", table: table, sql: "[Count] >= 0");
                """),
            new DiagnosticResult("BW0027", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("AddCheckConstraint", "a check constraint", "the table"));

    [Fact]
    public async Task StepsOnTableCreatedInSameMigration_AreNotReported()
        => await RunAsync(Up("""
            migrationBuilder.CreateTable(
                name: "Items",
                schema: "vault",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false),
                    Name = table.Column<string>(nullable: false),
                });
            migrationBuilder.CreateIndex(name: "IX_Items_Name", schema: "vault", table: "Items", column: "Name", unique: true);
            migrationBuilder.AddCheckConstraint(name: "CK_Items_Id", schema: "vault", table: "Items", sql: "[Id] > 0");
            migrationBuilder.AddColumn<int>(name: "Count", schema: "vault", table: "Items", nullable: false);
            """));

    [Fact]
    public async Task StepsBeforeTableIsCreated_AreReported()
        => await RunAsync(Up("""
            migrationBuilder.{|BW0026:RenameTable|}(name: "Items", newName: "ItemsOld");
            migrationBuilder.{|BW0026:DropColumn|}(name: "Legacy", table: "Things");
            migrationBuilder.{|BW0026:DropTable|}(name: "Things");
            migrationBuilder.CreateTable(name: "Items", columns: table => new { Id = table.Column<int>(nullable: false) });
            migrationBuilder.CreateTable(name: "Things", columns: table => new { Id = table.Column<int>(nullable: false) });
            migrationBuilder.CreateIndex(name: "IX_Things_Id", table: "Things", column: "Id", unique: true);
            """));

    [Fact]
    public async Task TableCreatedThenRenamed_StaysNew()
        => await RunAsync(Up("""
            migrationBuilder.CreateTable(name: "ItemsNew", columns: table => new { Id = table.Column<int>(nullable: false) });
            migrationBuilder.RenameTable(name: "ItemsNew", newName: "Items");
            migrationBuilder.AddForeignKey(name: "FK_Items_Owners", table: "Items", column: "Id", principalTable: "Owners", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            migrationBuilder.DropTable(name: "Items");
            migrationBuilder.{|BW0026:DropColumn|}(name: "Legacy", table: "Items");
            """));

    [Fact]
    public async Task TableCreatedThenMovedToAnotherSchema_StaysNew()
        => await RunAsync(Up("""
            migrationBuilder.CreateTable(name: "ItemsNew", schema: "a", columns: table => new { Id = table.Column<int>(nullable: false) });
            migrationBuilder.RenameTable(name: "ItemsNew", schema: "a", newName: "Items", newSchema: "b");
            migrationBuilder.DropColumn(name: "Legacy", schema: "b", table: "Items");
            migrationBuilder.{|BW0026:DropColumn|}(name: "Legacy", schema: "a", table: "Items");
            """));

    [Fact]
    public async Task SameTableNameInAnotherSchema_IsStillReported()
        => await RunAsync(Up("""
            migrationBuilder.CreateTable(
                name: "Items",
                schema: "vault",
                columns: table => new { Id = table.Column<int>(nullable: false) });
            migrationBuilder.{|BW0026:DropColumn|}(name: "Legacy", table: "Items");
            """));

    [Fact]
    public async Task MigrationMarkedAsGenerated_IsStillChecked()
        => await RunAsync("""
            // <auto-generated />
            using Microsoft.EntityFrameworkCore.Migrations;

            public partial class DropLegacy : Migration
            {
                protected override void Up(MigrationBuilder migrationBuilder)
                    => migrationBuilder.{|BW0026:DropColumn|}(name: "Legacy", table: "Items");
            }
            """);

    [Fact]
    public async Task Down_IsNotChecked()
        => await RunAsync("""
            using Microsoft.EntityFrameworkCore.Migrations;

            public partial class AddLegacy : Migration
            {
                protected override void Up(MigrationBuilder migrationBuilder)
                    => migrationBuilder.AddColumn<int>(name: "Legacy", table: "Items", nullable: true);

                protected override void Down(MigrationBuilder migrationBuilder)
                    => migrationBuilder.DropColumn(name: "Legacy", table: "Items");
            }
            """);

    [Fact]
    public async Task UpOutsideAMigration_IsNotChecked()
        => await RunAsync("""
            using Microsoft.EntityFrameworkCore.Migrations;

            public abstract class Step
            {
                protected abstract void Up(MigrationBuilder migrationBuilder);
            }

            public class Cleanup : Step
            {
                protected override void Up(MigrationBuilder migrationBuilder)
                    => migrationBuilder.DropColumn(name: "Legacy", table: "Items");
            }
            """);

    [Fact]
    public async Task Justification_SuppressesTheDiagnostic()
        => await RunAsync("""
            using System.Diagnostics.CodeAnalysis;
            using Microsoft.EntityFrameworkCore.Migrations;

            [SuppressMessage("Bitwarden.Database", "BW0026", Justification = "Items.Legacy has been unmapped since 2026.11.0.")]
            public partial class DropLegacy : Migration
            {
                protected override void Up(MigrationBuilder migrationBuilder)
                    => migrationBuilder.DropColumn(name: "Legacy", table: "Items");
            }
            """);

    /// <summary>
    /// A migration whose <c>Up</c> runs the given steps, with the case's description as a comment.
    /// </summary>
    private static string Up(string steps, string description = "")
        => $$"""
            using Microsoft.EntityFrameworkCore.Migrations;

            public partial class Change : Migration
            {
                // {{description}}
                protected override void Up(MigrationBuilder migrationBuilder)
                {
            {{steps}}
                }
            }
            """;

    /// <summary>
    /// The name of the step marked <c>{|#0:...|}</c>, which the message names first.
    /// </summary>
    private static string StepName(string step) => MarkedStep().Match(step).Groups[1].Value;

    [GeneratedRegex(@"\{\|#0:(\w+)\|\}")]
    private static partial Regex MarkedStep();

    private static async Task RunAsync([StringSyntax("C#-test")] string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<MigrationStepAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        test.TestState.ReferenceAssemblies = ReferenceAssemblies.Net.Net90;
        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(MigrationBuilder).Assembly.Location));
        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly.Location));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
