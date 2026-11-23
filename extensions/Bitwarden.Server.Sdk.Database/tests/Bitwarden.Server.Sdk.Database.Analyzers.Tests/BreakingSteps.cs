namespace Bitwarden.Server.Sdk.Database.Analyzers.Tests;

/// <summary>
/// Steps BW0026 reports, each with the reason its message gives. The step's name is marked
/// <c>{|#0:...|}</c>, where the diagnostic is expected.
/// </summary>
public sealed class BreakingSteps : TheoryData<string, string, string>
{
    private const string DropsColumn = "it drops a column the previous release may still read or write";
    private const string OldName = "the previous release still uses the old name";
    private const string RequiredWithoutDefault = "it adds a NOT NULL column with no default, which the previous release's inserts don't write";
    private const string NoOldShape = "it doesn't give the column's old shape (oldClrType and the other old values), so it can't be shown to leave the column usable";
    private const string ChangesType = "it changes the column's type";
    private const string MakesNotNull = "it makes the column NOT NULL, which the previous release may not write";
    private const string AllowsNull = "it lets the column hold NULL, which the previous release may map as required and fail to read";
    private const string RemovesDefault = "it removes the default from a NOT NULL column, which the previous release's inserts may rely on";
    private const string ChangesCollation = "it changes the column's collation, which changes how values compare and can rewrite the table";
    private const string ChangesCharSet = "it changes the column's character set";
    private const string ChangesStoreType = "it changes the column's store type";
    private const string Shortens = "it shortens the column";
    private const string ReducesPrecision = "it reduces the column's precision or scale";
    private const string ChangesComputation = "it changes how the column is computed";
    private const string RefusesDeletes = "the previous release can't delete a row of the table it refers to once the new table holds a reference to it";

    public BreakingSteps()
    {
        // Removing or renaming something the previous release uses.
        Add(
            "Dropping a column",
            """migrationBuilder.{|#0:DropColumn|}(name: "Legacy", table: "Items");""",
            DropsColumn);
        Add(
            "Dropping a table",
            """migrationBuilder.{|#0:DropTable|}(name: "Items");""",
            "it drops a table the previous release may still use");
        Add(
            "Renaming a column",
            """migrationBuilder.{|#0:RenameColumn|}(name: "Legacy", table: "Items", newName: "Current");""",
            OldName);
        Add(
            "Renaming a table",
            """migrationBuilder.{|#0:RenameTable|}(name: "Items", newName: "Things");""",
            OldName);
        Add(
            "Renaming a sequence",
            """migrationBuilder.{|#0:RenameSequence|}(name: "ItemNumbers", newName: "Numbers");""",
            OldName);
        Add(
            "Dropping a primary key",
            """migrationBuilder.{|#0:DropPrimaryKey|}(name: "PK_Items", table: "Items");""",
            "it changes the key the previous release identifies rows by");
        Add(
            "Dropping a schema",
            """migrationBuilder.{|#0:DropSchema|}(name: "legacy");""",
            "it drops a schema the previous release may still use");
        Add(
            "Dropping a sequence",
            """migrationBuilder.{|#0:DropSequence|}(name: "ItemNumbers");""",
            "it drops a sequence the previous release may still use");

        // Adding a column the previous release's inserts don't write.
        Add(
            "Adding a NOT NULL column with no default",
            """migrationBuilder.{|#0:AddColumn|}<int>(name: "Count", table: "Items", nullable: false);""",
            RequiredWithoutDefault);
        Add(
            "Adding a NOT NULL column whose default is null",
            """
            migrationBuilder.{|#0:AddColumn|}<string>(
                name: "Name", table: "Items", type: "nvarchar(100)", nullable: false, defaultValue: null);
            """,
            RequiredWithoutDefault);
        Add(
            "Adding a column whose nullability isn't a constant",
            """
            bool nullable = false;
            migrationBuilder.{|#0:AddColumn|}<int>(name: "Count", table: "Items", nullable: nullable);
            """,
            RequiredWithoutDefault);

        // Altering a column so the previous release can no longer use it.
        Add(
            "Altering a column written by hand, without its old shape",
            """migrationBuilder.{|#0:AlterColumn|}<string>(name: "Name", table: "Items", nullable: false);""",
            NoOldShape);
        Add(
            "Changing the CLR type",
            """
            migrationBuilder.{|#0:AlterColumn|}<long>(
                name: "Count", table: "Items", nullable: false,
                oldClrType: typeof(int));
            """,
            ChangesType);
        Add(
            "Making a column NOT NULL",
            """
            migrationBuilder.{|#0:AlterColumn|}<string>(
                name: "Name", table: "Items", nullable: false,
                oldClrType: typeof(string), oldNullable: true);
            """,
            MakesNotNull);
        Add(
            "Letting a NOT NULL column hold NULL",
            """
            migrationBuilder.{|#0:AlterColumn|}<string>(
                name: "Name", table: "Items", nullable: true,
                oldClrType: typeof(string));
            """,
            AllowsNull);
        Add(
            "Removing a NOT NULL column's default value",
            """
            migrationBuilder.{|#0:AlterColumn|}<bool>(
                name: "Enabled", table: "Items", type: "boolean", nullable: false,
                oldClrType: typeof(bool), oldType: "boolean", oldDefaultValue: true);
            """,
            RemovesDefault);
        Add(
            "Removing a NOT NULL column's default SQL",
            """
            migrationBuilder.{|#0:AlterColumn|}<System.DateTime>(
                name: "CreatedAt", table: "Items", type: "datetime2", nullable: false,
                oldClrType: typeof(System.DateTime), oldType: "datetime2", oldDefaultValueSql: "GETUTCDATE()");
            """,
            RemovesDefault);
        Add(
            "Changing a ciphertext column's collation to an exact comparison",
            """
            migrationBuilder.{|#0:AlterColumn|}<string>(
                name: "Data", table: "Cipher", type: "nvarchar(max)", nullable: false,
                collation: "Latin1_General_100_BIN2_UTF8",
                oldClrType: typeof(string), oldType: "nvarchar(max)",
                oldCollation: "Latin1_General_100_CI_AS_SC_UTF8");
            """,
            ChangesCollation);
        Add(
            "Changing the collation Pomelo scaffolds as an old annotation",
            """
            migrationBuilder.{|#0:AlterColumn|}<string>(
                name: "FileName", table: "Attachments", type: "longtext", nullable: true,
                collation: "utf8mb4_bin",
                oldClrType: typeof(string), oldType: "longtext", oldNullable: true)
                .OldAnnotation("Relational:Collation", "utf8mb4_general_ci");
            """,
            ChangesCollation);
        Add(
            "Changing a MySQL character set",
            """
            migrationBuilder.{|#0:AlterColumn|}<string>(
                name: "Name", table: "Items", type: "longtext", nullable: false,
                oldClrType: typeof(string), oldType: "longtext")
                .Annotation("MySql:CharSet", "latin1")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
            """,
            ChangesCharSet);
        Add(
            "Changing to a related store type",
            """
            migrationBuilder.{|#0:AlterColumn|}<string>(
                name: "Code", table: "Items", type: "varchar(10)", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(10)");
            """,
            ChangesStoreType);
        Add(
            "Bounding an unbounded PostgreSQL column",
            """
            migrationBuilder.{|#0:AlterColumn|}<string>(
                name: "Code", table: "Items", type: "character varying(10)", maxLength: 10, nullable: false,
                oldClrType: typeof(string), oldType: "text");
            """,
            ChangesStoreType);
        Add(
            "Shortening a store type's length",
            """
            migrationBuilder.{|#0:AlterColumn|}<string>(
                name: "Name", table: "Items", type: "nvarchar(50)", maxLength: 50, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(100)", oldMaxLength: 100);
            """,
            ChangesStoreType);
        Add(
            "Bounding a SQL Server max column",
            """
            migrationBuilder.{|#0:AlterColumn|}<string>(
                name: "Name", table: "Items", type: "nvarchar(100)", maxLength: 100, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(max)");
            """,
            ChangesStoreType);
        Add(
            "Reducing the scale in a decimal store type",
            """
            migrationBuilder.{|#0:AlterColumn|}<decimal>(
                name: "Price", table: "Items", type: "decimal(18,4)", precision: 18, scale: 4, nullable: false,
                oldClrType: typeof(decimal), oldType: "decimal(18,2)", oldPrecision: 18, oldScale: 2);
            """,
            ChangesStoreType);
        Add(
            "Changing a store type whose size isn't a number (PostGIS's SRID)",
            """
            migrationBuilder.{|#0:AlterColumn|}<string>(
                name: "Area", table: "Items", type: "geometry(Point,3857)", nullable: false,
                oldClrType: typeof(string), oldType: "geometry(Point,4326)");
            """,
            ChangesStoreType);
        Add(
            "Giving a store type with no old type to compare against",
            """
            migrationBuilder.{|#0:AlterColumn|}<string>(
                name: "Name", table: "Items", type: "nvarchar(200)", nullable: false,
                oldClrType: typeof(string));
            """,
            ChangesStoreType);
        Add(
            "Shortening a column whose store type stays the same (SQLite)",
            """
            migrationBuilder.{|#0:AlterColumn|}<string>(
                name: "Name", table: "Items", type: "TEXT", maxLength: 50, nullable: false,
                oldClrType: typeof(string), oldType: "TEXT", oldMaxLength: 100);
            """,
            Shortens);
        Add(
            "Shortening a column by a length that isn't a constant",
            """
            int length = 50;
            migrationBuilder.{|#0:AlterColumn|}<string>(
                name: "Name", table: "Items", maxLength: length, nullable: false,
                oldClrType: typeof(string), oldMaxLength: 100);
            """,
            Shortens);
        Add(
            "Reducing precision with no store type",
            """
            migrationBuilder.{|#0:AlterColumn|}<decimal>(
                name: "Price", table: "Items", precision: 10, scale: 2, nullable: false,
                oldClrType: typeof(decimal), oldPrecision: 18, oldScale: 2);
            """,
            ReducesPrecision);
        Add(
            "Reducing scale with no store type",
            """
            migrationBuilder.{|#0:AlterColumn|}<decimal>(
                name: "Price", table: "Items", precision: 18, scale: 1, nullable: false,
                oldClrType: typeof(decimal), oldPrecision: 18, oldScale: 2);
            """,
            ReducesPrecision);
        Add(
            "Changing a computed column's expression",
            """
            migrationBuilder.{|#0:AlterColumn|}<int>(
                name: "Total", table: "Items", computedColumnSql: "[A] + [B]", nullable: false,
                oldClrType: typeof(int), oldComputedColumnSql: "[A]");
            """,
            ChangesComputation);

        // A new table's foreign key to a table the previous release already uses.
        Add(
            "A new table's foreign key, scaffolded in CreateTable, with no onDelete",
            """
            migrationBuilder.CreateTable(
                name: "ItemTags",
                columns: table => new { Id = table.Column<int>(nullable: false), ItemId = table.Column<int>(nullable: true) },
                constraints: table => table.{|#0:ForeignKey|}(
                    name: "FK_ItemTags_Items", column: x => x.ItemId,
                    principalTable: "Items", principalColumn: "Id"));
            """,
            RefusesDeletes);
        Add(
            "A new table's foreign key, added after CreateTable, that restricts deletes",
            """
            migrationBuilder.CreateTable(
                name: "ItemTags",
                columns: table => new { Id = table.Column<int>(nullable: false), OwnerId = table.Column<int>(nullable: true) });
            migrationBuilder.{|#0:AddForeignKey|}(
                name: "FK_ItemTags_Owners", table: "ItemTags", column: "OwnerId",
                principalTable: "Owners", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            """,
            RefusesDeletes);
        Add(
            "A new table's foreign key whose onDelete isn't a constant",
            """
            var onDelete = ReferentialAction.Cascade;
            migrationBuilder.CreateTable(
                name: "ItemTags",
                columns: table => new { Id = table.Column<int>(nullable: false), OwnerId = table.Column<int>(nullable: true) });
            migrationBuilder.{|#0:AddForeignKey|}(
                name: "FK_ItemTags_Owners", table: "ItemTags", column: "OwnerId",
                principalTable: "Owners", principalColumn: "Id", onDelete: onDelete);
            """,
            RefusesDeletes);
    }
}
