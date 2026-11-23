namespace Bitwarden.Server.Sdk.Database.Analyzers.Tests;

/// <summary>
/// Steps BW0027 reports, each with the constraint its message names. The step's name is marked
/// <c>{|#0:...|}</c>, where the diagnostic is expected.
/// </summary>
public sealed class ConstraintSteps : TheoryData<string, string, string>
{
    public ConstraintSteps()
    {
        Add(
            "Adding a unique index",
            """
            migrationBuilder.{|#0:CreateIndex|}(
                name: "IX_Items_Name", table: "Items", column: "Name", unique: true);
            """,
            "a unique index");
        Add(
            "Adding an index whose uniqueness isn't a constant",
            """
            bool unique = true;
            migrationBuilder.{|#0:CreateIndex|}(
                name: "IX_Items_Name", table: "Items", column: "Name", unique: unique);
            """,
            "an index that may be unique");
        Add(
            "Adding a unique constraint",
            """
            migrationBuilder.{|#0:AddUniqueConstraint|}(
                name: "AK_Items_Name", table: "Items", column: "Name");
            """,
            "a unique constraint");
        Add(
            "Adding a check constraint",
            """
            migrationBuilder.{|#0:AddCheckConstraint|}(
                name: "CK_Items_Count", table: "Items", sql: "[Count] >= 0");
            """,
            "a check constraint");
        Add(
            "Adding a foreign key",
            """
            migrationBuilder.{|#0:AddForeignKey|}(
                name: "FK_Items_Owners", table: "Items", column: "OwnerId",
                principalTable: "Owners", principalColumn: "Id");
            """,
            "a foreign key");
    }
}
