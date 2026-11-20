using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Bitwarden.Server.Sdk.Database.Analyzers.Tests;

public class MigrationStepAnalyzerTests
{
    [Theory]
    [InlineData("""migrationBuilder.{|BW0026:DropColumn|}(name: "Legacy", table: "Items");""")]
    [InlineData("""migrationBuilder.{|BW0026:DropTable|}(name: "Items");""")]
    [InlineData("""migrationBuilder.{|BW0026:RenameColumn|}(name: "Legacy", table: "Items", newName: "Current");""")]
    [InlineData("""migrationBuilder.{|BW0026:RenameTable|}(name: "Items", newName: "Things");""")]
    [InlineData("""migrationBuilder.{|BW0026:DropPrimaryKey|}(name: "PK_Items", table: "Items");""")]
    [InlineData("""migrationBuilder.{|BW0026:DropSchema|}(name: "legacy");""")]
    [InlineData("""migrationBuilder.{|BW0026:DropSequence|}(name: "ItemNumbers");""")]
    [InlineData("""migrationBuilder.{|BW0026:RenameSequence|}(name: "ItemNumbers", newName: "Numbers");""")]
    public async Task RemovingOrRenaming_IsReported(string step)
        => await RunAsync(Up(step));

    [Theory]
    [InlineData("""migrationBuilder.{|BW0026:AddColumn|}<int>(name: "Count", table: "Items", nullable: false);""")]
    [InlineData("""migrationBuilder.{|BW0026:AddColumn|}<string>(name: "Name", table: "Items", type: "nvarchar(100)", nullable: false, defaultValue: null);""")]
    public async Task AddingRequiredColumnWithoutDefault_IsReported(string step)
        => await RunAsync(Up(step));

    [Theory]
    [InlineData("""migrationBuilder.AddColumn<int>(name: "Count", table: "Items", nullable: true);""")]
    [InlineData("""migrationBuilder.AddColumn<int>(name: "Count", table: "Items", nullable: false, defaultValue: 0);""")]
    [InlineData("""migrationBuilder.AddColumn<string>(name: "Name", table: "Items", nullable: false, defaultValue: "");""")]
    [InlineData("""migrationBuilder.AddColumn<System.DateTime>(name: "At", table: "Items", nullable: false, defaultValue: new System.DateTime(2026, 1, 1));""")]
    [InlineData("""migrationBuilder.AddColumn<System.DateTime>(name: "At", table: "Items", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP");""")]
    [InlineData("""migrationBuilder.AddColumn<byte[]>(name: "Version", table: "Items", rowVersion: true, nullable: false);""")]
    public async Task AddingColumnTheOldReleaseCanIgnore_IsNotReported(string step)
        => await RunAsync(Up(step));

    [Theory]
    // The collation change scaffolded for a ciphertext column moving to an exact comparison.
    [InlineData("""migrationBuilder.{|BW0026:AlterColumn|}<string>(name: "Data", table: "Cipher", type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2_UTF8", oldClrType: typeof(string), oldType: "nvarchar(max)", oldCollation: "Latin1_General_100_CI_AS_SC_UTF8");""")]
    [InlineData("""migrationBuilder.{|BW0026:AlterColumn|}<string>(name: "FileName", table: "Attachments", type: "longtext", nullable: true, collation: "utf8mb4_bin", oldClrType: typeof(string), oldType: "longtext").OldAnnotation("Relational:Collation", "utf8mb4_general_ci");""")]
    [InlineData("""migrationBuilder.{|BW0026:AlterColumn|}<string>(name: "Name", table: "Items", type: "nvarchar(50)", maxLength: 50, nullable: false, oldClrType: typeof(string), oldType: "nvarchar(100)", oldMaxLength: 100);""")]
    [InlineData("""migrationBuilder.{|BW0026:AlterColumn|}<string>(name: "Name", table: "Items", type: "nvarchar(100)", maxLength: 100, nullable: false, oldClrType: typeof(string), oldType: "nvarchar(max)");""")]
    [InlineData("""migrationBuilder.{|BW0026:AlterColumn|}<string>(name: "Name", table: "Items", nullable: false, oldClrType: typeof(string), oldNullable: true);""")]
    [InlineData("""migrationBuilder.{|BW0026:AlterColumn|}<long>(name: "Count", table: "Items", nullable: false, oldClrType: typeof(int));""")]
    [InlineData("""migrationBuilder.{|BW0026:AlterColumn|}<string>(name: "Code", table: "Items", type: "varchar(10)", nullable: false, oldClrType: typeof(string), oldType: "nvarchar(10)");""")]
    [InlineData("""migrationBuilder.{|BW0026:AlterColumn|}<decimal>(name: "Price", table: "Items", precision: 10, scale: 2, nullable: false, oldClrType: typeof(decimal), oldPrecision: 18, oldScale: 2);""")]
    [InlineData("""migrationBuilder.{|BW0026:AlterColumn|}<int>(name: "Total", table: "Items", computedColumnSql: "[A] + [B]", nullable: false, oldClrType: typeof(int), oldComputedColumnSql: "[A]");""")]
    public async Task NarrowingColumn_IsReported(string step)
        => await RunAsync(Up(step));

    [Theory]
    [InlineData("""migrationBuilder.AlterColumn<string>(name: "Name", table: "Items", type: "nvarchar(200)", maxLength: 200, nullable: false, oldClrType: typeof(string), oldType: "nvarchar(100)", oldMaxLength: 100);""")]
    [InlineData("""migrationBuilder.AlterColumn<string>(name: "Name", table: "Items", type: "nvarchar(max)", nullable: false, oldClrType: typeof(string), oldType: "nvarchar(100)", oldMaxLength: 100);""")]
    [InlineData("""migrationBuilder.AlterColumn<string>(name: "Name", table: "Items", nullable: true, oldClrType: typeof(string));""")]
    [InlineData("""migrationBuilder.AlterColumn<int>(name: "Count", table: "Items", nullable: false, defaultValue: 1, oldClrType: typeof(int), oldDefaultValue: 0);""")]
    [InlineData("""migrationBuilder.AlterColumn<int>(name: "Count", table: "Items", nullable: true, oldClrType: typeof(int), oldNullable: true);""")]
    // Pomelo's MySQL provider scaffolds the unchanged collation as an annotation, not oldCollation.
    [InlineData("""migrationBuilder.AlterColumn<string>(name: "FileName", table: "Attachments", type: "longtext", nullable: true, collation: "utf8mb4_bin", oldClrType: typeof(string), oldType: "longtext").OldAnnotation("Relational:Collation", "utf8mb4_bin");""")]
    public async Task WideningOrUnrelatedColumnChange_IsNotReported(string step)
        => await RunAsync(Up(step));

    [Theory]
    [InlineData("""migrationBuilder.{|BW0027:CreateIndex|}(name: "IX_Items_Name", table: "Items", column: "Name", unique: true);""")]
    [InlineData("""migrationBuilder.{|BW0027:AddUniqueConstraint|}(name: "AK_Items_Name", table: "Items", column: "Name");""")]
    [InlineData("""migrationBuilder.{|BW0027:AddCheckConstraint|}(name: "CK_Items_Count", table: "Items", sql: "[Count] >= 0");""")]
    public async Task AddingRuleToExistingTable_IsReported(string step)
        => await RunAsync(Up(step));

    [Theory]
    [InlineData("""migrationBuilder.CreateIndex(name: "IX_Items_Name", table: "Items", column: "Name");""")]
    [InlineData("""migrationBuilder.DropIndex(name: "IX_Items_Name", table: "Items");""")]
    [InlineData("""migrationBuilder.DropForeignKey(name: "FK_Items_Owners", table: "Items");""")]
    [InlineData("""migrationBuilder.DropUniqueConstraint(name: "AK_Items_Name", table: "Items");""")]
    [InlineData("""migrationBuilder.RenameIndex(name: "IX_Old", table: "Items", newName: "IX_New");""")]
    [InlineData("""migrationBuilder.EnsureSchema(name: "vault");""")]
    [InlineData("""migrationBuilder.Sql("UPDATE [Items] SET [Count] = 0");""")]
    public async Task StepTheOldReleaseCanIgnore_IsNotReported(string step)
        => await RunAsync(Up(step));

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
    public async Task SameTableNameInAnotherSchema_IsStillReported()
        => await RunAsync(Up("""
            migrationBuilder.CreateTable(
                name: "Items",
                schema: "vault",
                columns: table => new { Id = table.Column<int>(nullable: false) });
            migrationBuilder.{|BW0026:DropColumn|}(name: "Legacy", table: "Items");
            """));

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

    private static string Up(string steps)
        => $$"""
            using Microsoft.EntityFrameworkCore.Migrations;

            public partial class Change : Migration
            {
                protected override void Up(MigrationBuilder migrationBuilder)
                {
                    {{steps}}
                }
            }
            """;

    private static async Task RunAsync([StringSyntax("C#-test")] string source)
    {
        var test = new CSharpAnalyzerTest<MigrationStepAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        };
        test.TestState.ReferenceAssemblies = ReferenceAssemblies.Net.Net90;
        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(MigrationBuilder).Assembly.Location));
        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly.Location));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
