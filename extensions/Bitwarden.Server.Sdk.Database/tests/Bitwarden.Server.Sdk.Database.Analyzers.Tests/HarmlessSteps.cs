namespace Bitwarden.Server.Sdk.Database.Analyzers.Tests;

/// <summary>
/// Steps the previous release can run against, which neither diagnostic reports.
/// </summary>
public sealed class HarmlessSteps : TheoryData<string, string>
{
    public HarmlessSteps()
    {
        // Columns the previous release's inserts can leave out.
        Add(
            "Adding a nullable column",
            """migrationBuilder.AddColumn<int>(name: "Count", table: "Items", nullable: true);""");
        Add(
            "Adding a NOT NULL column with a default value",
            """migrationBuilder.AddColumn<int>(name: "Count", table: "Items", nullable: false, defaultValue: 0);""");
        Add(
            "Adding a NOT NULL string with an empty default",
            """migrationBuilder.AddColumn<string>(name: "Name", table: "Items", nullable: false, defaultValue: "");""");
        Add(
            "Adding a NOT NULL column whose default isn't a constant",
            """
            migrationBuilder.AddColumn<System.DateTime>(
                name: "At", table: "Items", nullable: false,
                defaultValue: new System.DateTime(2026, 1, 1));
            """);
        Add(
            "Adding a NOT NULL column with default SQL",
            """
            migrationBuilder.AddColumn<System.DateTime>(
                name: "At", table: "Items", nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");
            """);
        Add(
            "Adding a row version, which the database fills",
            """migrationBuilder.AddColumn<byte[]>(name: "Version", table: "Items", rowVersion: true, nullable: false);""");

        // Column changes that keep everything the previous release reads and writes.
        Add(
            "Lengthening a column",
            """
            migrationBuilder.AlterColumn<string>(
                name: "Name", table: "Items", type: "nvarchar(200)", maxLength: 200, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(100)", oldMaxLength: 100);
            """);
        Add(
            "Removing a SQL Server column's length limit",
            """
            migrationBuilder.AlterColumn<string>(
                name: "Name", table: "Items", type: "nvarchar(max)", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(100)", oldMaxLength: 100);
            """);
        Add(
            "Removing a PostgreSQL column's length limit",
            """
            migrationBuilder.AlterColumn<string>(
                name: "Name", table: "Items", type: "text", nullable: false,
                oldClrType: typeof(string), oldType: "character varying(100)", oldMaxLength: 100);
            """);
        Add(
            "Removing a MySQL column's length limit",
            """
            migrationBuilder.AlterColumn<string>(
                name: "Domains", table: "InviteLinks", type: "longtext", nullable: false,
                oldClrType: typeof(string), oldType: "varchar(2000)", oldMaxLength: 2000)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
            """);
        Add(
            "Removing a MySQL binary column's length limit",
            """
            migrationBuilder.AlterColumn<byte[]>(
                name: "Blob", table: "Items", type: "longblob", nullable: false,
                oldClrType: typeof(byte[]), oldType: "varbinary(100)", oldMaxLength: 100);
            """);
        Add(
            "Raising a decimal's precision",
            """
            migrationBuilder.AlterColumn<decimal>(
                name: "Price", table: "Items", type: "decimal(20,2)", precision: 20, scale: 2, nullable: false,
                oldClrType: typeof(decimal), oldType: "decimal(18,2)", oldPrecision: 18, oldScale: 2);
            """);
        Add(
            "Raising a PostgreSQL numeric's precision and scale",
            """
            migrationBuilder.AlterColumn<decimal>(
                name: "Price", table: "Items", type: "numeric(20,4)", precision: 20, scale: 4, nullable: false,
                oldClrType: typeof(decimal), oldType: "numeric(18,2)", oldPrecision: 18, oldScale: 2);
            """);
        Add(
            "Raising a PostgreSQL timestamp's precision",
            """
            migrationBuilder.AlterColumn<System.DateTimeOffset>(
                name: "At", table: "Items", type: "timestamp(6) with time zone", precision: 6, nullable: false,
                oldClrType: typeof(System.DateTimeOffset), oldType: "timestamp(3) with time zone", oldPrecision: 3);
            """);
        Add(
            "Changing a NOT NULL column's default to another",
            """
            migrationBuilder.AlterColumn<int>(
                name: "Count", table: "Items", nullable: false, defaultValue: 1,
                oldClrType: typeof(int), oldDefaultValue: 0);
            """);
        Add(
            "Removing a nullable column's default",
            """
            migrationBuilder.AlterColumn<int>(
                name: "Count", table: "Items", nullable: true,
                oldClrType: typeof(int), oldNullable: true, oldDefaultValue: 0);
            """);
        Add(
            "Keeping the collation Pomelo scaffolds as an old annotation",
            """
            migrationBuilder.AlterColumn<string>(
                name: "FileName", table: "Attachments", type: "longtext", nullable: true,
                collation: "utf8mb4_bin",
                oldClrType: typeof(string), oldType: "longtext", oldNullable: true)
                .OldAnnotation("Relational:Collation", "utf8mb4_bin");
            """);

        // Steps that take nothing away from the previous release.
        Add(
            "Adding an index that isn't unique",
            """migrationBuilder.CreateIndex(name: "IX_Items_Name", table: "Items", column: "Name");""");
        Add(
            "Dropping an index",
            """migrationBuilder.DropIndex(name: "IX_Items_Name", table: "Items");""");
        Add(
            "Dropping a foreign key",
            """migrationBuilder.DropForeignKey(name: "FK_Items_Owners", table: "Items");""");
        Add(
            "Dropping a unique constraint",
            """migrationBuilder.DropUniqueConstraint(name: "AK_Items_Name", table: "Items");""");
        Add(
            "Renaming an index",
            """migrationBuilder.RenameIndex(name: "IX_Old", table: "Items", newName: "IX_New");""");
        Add(
            "Ensuring a schema exists",
            """migrationBuilder.EnsureSchema(name: "vault");""");
        Add(
            "Running SQL, which the analyzer doesn't read",
            """migrationBuilder.Sql("UPDATE [Items] SET [Count] = 0");""");

        // A new table's foreign keys that leave the previous release's deletes working.
        Add(
            "A new table's foreign key to an existing table that cascades deletes",
            """
            migrationBuilder.CreateTable(
                name: "ItemTags",
                columns: table => new { Id = table.Column<int>(nullable: false), ItemId = table.Column<int>(nullable: false) },
                constraints: table => table.ForeignKey(
                    name: "FK_ItemTags_Items", column: x => x.ItemId,
                    principalTable: "Items", principalColumn: "Id", onDelete: ReferentialAction.Cascade));
            """);
        Add(
            "A new table's foreign key to an existing table that sets the reference to NULL",
            """
            migrationBuilder.CreateTable(
                name: "ItemTags",
                columns: table => new { Id = table.Column<int>(nullable: false), OwnerId = table.Column<int>(nullable: true) });
            migrationBuilder.AddForeignKey(
                name: "FK_ItemTags_Owners", table: "ItemTags", column: "OwnerId",
                principalTable: "Owners", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
            """);
        Add(
            "A new table's foreign key to itself",
            """
            migrationBuilder.CreateTable(
                name: "ItemTags",
                columns: table => new { Id = table.Column<int>(nullable: false), ParentId = table.Column<int>(nullable: true) },
                constraints: table => table.ForeignKey(
                    name: "FK_ItemTags_ItemTags", column: x => x.ParentId,
                    principalTable: "ItemTags", principalColumn: "Id"));
            """);
        Add(
            "A new table's foreign key to another table created in the same Up",
            """
            migrationBuilder.CreateTable(
                name: "Tags", schema: "catalog",
                columns: table => new { Id = table.Column<int>(nullable: false) });
            migrationBuilder.CreateTable(
                name: "ItemTags",
                columns: table => new { Id = table.Column<int>(nullable: false), TagId = table.Column<int>(nullable: true) });
            migrationBuilder.AddForeignKey(
                name: "FK_ItemTags_Tags", table: "ItemTags", column: "TagId",
                principalTable: "Tags", principalSchema: "catalog", principalColumn: "Id");
            """);
    }
}
