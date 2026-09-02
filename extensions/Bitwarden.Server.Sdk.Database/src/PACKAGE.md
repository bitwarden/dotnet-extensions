# Bitwarden.Server.Sdk.Database

## About

This package, combined with the `DatabaseSetupGenerator` source generator that ships inside it,
stands up a complete multi-provider database migration pipeline from a single assembly attribute.
Applying `[assembly: DatabaseSetup<TContext>]` to a project causes the generator to emit:

- Provider-specific `DbContext` subclasses (`SqliteContext`, `SqlServerContext`, etc.)
- EF Core design-time factories for each provider (used by `dotnet ef` tooling)
- A `Add{Schema}` DI extension method
- A `Program` entry point that handles the migration CLI

At runtime the package provides:

- An EF Core migrations runner (SQLite, PostgreSQL, MySQL via the Npgsql and Pomelo providers)
- A DbUp migrations runner (SQL Server, using embedded `.sql` scripts)
- A hosted service that applies pending migrations automatically on self-hosted startup
- `IDatabaseMigrator` — a keyed service you can resolve to run migrations on demand

## Getting started

### 1. Create the schema project

A schema lives in its own project, which builds into a migration executable. The one
schema-specific decision is which EF Core providers it supports:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.8" />
  <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.4" />
</ItemGroup>
```

A provider gets a subcontext, a design-time factory, and a switch case only if its package is
referenced here, so this list decides which providers the generated code supports.

Scripts need no csproj entry: `.sql` under `Migrations/SqlServer/` and under any `HelperScripts/`
folder is embedded by convention, so adding a script is just adding a file.

> The rest of the project — turning the pipeline on, wiring the generator, and naming the embedded
> scripts — is [project setup](#project-setup), a single property under the SDK. Everything below
> assumes it is in place.

### 2. Define your DbContext and apply the attribute

```csharp
using Bitwarden.Server.Sdk.Database;
using Microsoft.EntityFrameworkCore;

[assembly: DatabaseSetup<OrderDatabaseContext>(MigratorKey = "Order")]

namespace Acme.OrderDatabase;

public class OrderDatabaseContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Order> Orders { get; set; }
}
```

The source generator derives the schema name from the context type name
(`OrderDatabaseContext` → schema `OrderDatabase`, default key `Order`). Override the key with
`MigratorKey` when the default doesn't match your configuration key.

### 3. Register in your host application

```csharp
// Configure connection options (named by migrator key)
builder.Services.Configure<DatabaseOptions>("Order", builder.Configuration.GetSection("OrderDatabase"));

// Register migrations services (generated extension method)
builder.Services.AddOrderDatabase();
```

```json
{
  "OrderDatabase": {
    "Provider": "PostgreSql",
    "ConnectionString": "Host=localhost;Database=orders"
  }
}
```

Valid `Provider` values: `Sqlite`, `SqlServer`, `PostgreSql`, `MySql`.

### 4. Migrating on startup

Registering a schema says nothing about who applies it. In the one service that owns a schema, ask
for it:

```csharp
builder.Services.AutoMigrateWhenSelfHosted("Order");
```

That applies pending migrations for `Order` as the host starts, on self-hosted instances only —
Bitwarden-managed ones migrate as a separate deployment step, using the migration executable this
project builds. Services that merely read the schema call nothing and leave it alone.

Only the initial phase runs on startup; a transition phase is a deployment step, not a boot-time
one, and the runner refuses it.

## Adding EF Core migrations

For SQLite, PostgreSQL, and MySQL, EF Core manages schema changes through migration files. Run the
following from the schema project directory in a **Debug** build:

```bash
dotnet run -- add-migration <MigrationName>
```

This calls `dotnet ef migrations add` for each EF provider (SQLite, PostgreSQL, MySQL) and places
the generated files under `Migrations/Sqlite/`, `Migrations/PostgreSql/`, and `Migrations/MySql/`.
SQL Server is not among them — it is migrated by DbUp from hand-authored scripts, so no subcontext
or design-time factory is generated for it and `dotnet ef` has nothing to target. EF migration calls
against a SQL Server-configured context throw rather than silently applying nothing:

```
MyDatabase migrates SQL Server with DbUp, not EF Core migrations.
Use the MyDatabase migrator instead of DbContext.Database calls.
```

Alternatively you can call `dotnet ef` directly, targeting one of the generated provider contexts:

```bash
dotnet ef migrations add <MigrationName> --context SqliteContext --output-dir Migrations/Sqlite
```

### Raw SQL in an EF Core migration

Some changes — a trigger, a batched backfill, anything provider-specific — are easier to write as
SQL than as `MigrationBuilder` calls, and a `.sql` file gets editor support that a C# string literal
doesn't. Put one under the provider's `HelperScripts` folder and run it by name:

```
Migrations/
  Sqlite/
    HelperScripts/
      2026-08-19_00_BackfillEmail.sql
  MySql/
    HelperScripts/
      2026-08-19_00_BackfillEmail.sql
```

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
    => migrationBuilder.SqlFromHelperScript("2026-08-19_00_BackfillEmail.sql");
```

The same call resolves per provider, so a migration written once picks the SQLite file when running
on SQLite and the MySQL file on MySQL. A script that works everywhere needs only one copy — put it
in any `HelperScripts` folder and every provider finds it.

Scripts are embedded by convention, so adding one needs no csproj change, and they are excluded from
the SQL Server script naming, so they can never be picked up as migrations DbUp should apply. A name
that doesn't resolve throws immediately, listing what is embedded, rather than failing part-way
through a migration run.

## SQL Server migrations and `.sqlproj`

SQL Server uses [DbUp](https://dbup.readthedocs.io/) with hand-authored `.sql` scripts rather than
EF Core migrations. Each script must be:

1. Placed under `Migrations/SqlServer/`, or `Migrations/SqlServer/Transition/` for a transition
   phase backfill. `Migrations/SqlServer/Archive/` is excluded from every phase.
2. Named with a sortable timestamp prefix so DbUp applies them in order:
   `2026-08-12_00_CreateOrdersTable.sql`

Embedding is automatic for all three folders — the nesting is what selects the phase.

Because these are plain `.sql` files you should manage them with a
[SQL Server Data Tools (`.sqlproj`)](https://learn.microsoft.com/en-us/sql/tools/sql-database-projects/sql-database-projects)
project. A `.sqlproj` provides syntax validation, IntelliSense, and schema diffing in Visual Studio
and Azure Data Studio without requiring a live database connection. The `.sqlproj` is for authoring
only — the `.csproj` remains the build and runtime home for the scripts.

> **Note:** DbUp records each applied script by name in a `dbo.Migration` journal table. Once a
> script has been applied, renaming or deleting it will make it look pending again; add a new
> script to make further changes.

### Adopting an existing journal

The journal name of a script is its embedded resource name, which includes the namespace of the
assembly it was embedded in. Scripts moving into a schema project therefore change names, and every
already-applied script looks pending. Set `BitSqlServerScriptPrefix` to the prefix the journal
already holds and the scripts are embedded under their original names, so the existing rows keep
matching and the journal is left alone:

```xml
<PropertyGroup>
  <BitSqlServerScriptPrefix>Bit.Migrator.DbScripts</BitSqlServerScriptPrefix>
</PropertyGroup>
```

Only what precedes the file name is set; the file name itself is untouched. A script that would
default to `Bit.OrderDatabase.SqlServer.2026-08-12_00_Foo.sql` is embedded — and journaled — as
`Bit.Migrator.DbScripts.2026-08-12_00_Foo.sql`. Nested folders keep their own segments, so
`Transition/x.sql` and `Archive/x.sql` land under `.DbScripts.Transition.` and `.DbScripts.Archive.`
and are selected or excluded accordingly.

| Property | Default | Description |
|----------|---------|-------------|
| `BitSqlServerScriptPrefix` | `$(RootNamespace).SqlServer` | The recorded name, everything ahead of the file name |
| `BitPreviousSqlServerScriptPrefix` | *(unset)* | Prefix an inherited journal may still record scripts under; unset means no rewriting |

The prefix describes the **recorded name**, not where the files live: scripts always sit in
`Migrations/SqlServer/`, and naming is applied to every script in a database project, so a resource
name never depends on its path. It is also passed through to the migrator, so the name scripts are
embedded under and the name it looks for can't drift. Folders below the script folder are preserved
as extra segments, so `Migrations/SqlServer/Archive/x.sql` is embedded as `<prefix>.Archive.x.sql`.

A schema inheriting a journal moves its scripts into that standard layout like any other, and points
the prefix at the name the journal already holds — copy it from a row, minus the file name:

```xml
<PropertyGroup>
  <BitSqlServerScriptPrefix>Bit.Migrator.DbScripts</BitSqlServerScriptPrefix>
</PropertyGroup>
```

The source folder covers every phase, since the nesting is what selects them.

#### Rewriting recorded names

Reproducing the recorded names covers a database whose journal was written under one naming scheme.
It cannot cover a database that might hold names from *several* eras, because
`BitSqlServerScriptPrefix` takes a single value — an instance upgrading from far enough back could hold rows no current build
reproduces.

For that case, name the prefix those older rows carry, next to the one it complements:

```xml
<PropertyGroup>
  <BitSqlServerScriptPrefix>Bit.Migrator.DbScripts</BitSqlServerScriptPrefix>
  <BitPreviousSqlServerScriptPrefix>Bit.Setup.DbScripts</BitPreviousSqlServerScriptPrefix>
</PropertyGroup>
```

Recorded names carrying it are rewritten onto `BitSqlServerScriptPrefix`, so
`Bit.Setup.DbScripts.<file>.sql` becomes `Bit.Migrator.DbScripts.<file>.sql`. Because both ends are
full prefixes, this also covers a journal whose folder segment differed, not just its namespace.

It is a build property rather than a runtime setting because it describes the schema's history, not
a deployment: every host that migrates this schema needs it, and one forgetting to configure it
would silently lose the protection. The generated `Add{Schema}Database` applies it.

The rewrite is part of preparing the database, so it runs before each migration — and before a dry
run, as in the migrator this replaces. It touches only rows containing the old namespace and is a
no-op once applied — including in environments whose journals never held the old
names. Values are passed as parameters; only the journal schema and
table, which this package fixes, are part of the statement. Reach for this only when a build-time
value genuinely can't match every row — a rewrite mutates deployment history, while the build-time
properties leave it untouched.

#### Script layout for an adopted schema

Scripts take the standard layout; only the recorded name keeps the folder the journal knows:

```
OrderDatabase/
  Migrations/
    SqlServer/                        ← recorded as Bit.Migrator.DbScripts.* via the prefix
      2026-08-01_00_Procedures.sql    ← journal name unchanged
      Transition/                     ← was DbScripts_transition/
        2026-08-19_00_Backfill.sql
      Archive/                        ← excluded from every phase
        2020-01-01_00_Old.sql
```

Transition scripts can move freely because that phase runs unjournaled — nothing recorded their old
names. Only the scripts directly under `DbScripts/` have names the journal already holds, and those
stay exactly as they are.

A finalization folder needs no home here: those scripts are folded into the main folder by the
release process, arriving as ordinary initial-phase scripts under new names.

Nothing about the layout is legacy-shaped — the scripts move to `Migrations/SqlServer/` and the
recorded name is reproduced by the two properties above.

## Moving an existing schema into a schema project

A schema that already has migrations elsewhere — often one project per provider, plus a separate
project holding the SQL scripts — can move in without rewriting its history. What follows is what
has to line up.

### Migration namespaces

Migrations and model snapshots are matched to a provider by their **namespace**, not by which
assembly or folder they came from. Each namespace segment is checked for a known provider prefix,
case-insensitively, so migrations authored in a per-provider assembly keep the namespaces they
already have:

| Namespace segment starts with | Resolves to |
|-------------------------------|-------------|
| `Sqlite` | `Sqlite` |
| `SqlServer` | `SqlServer` |
| `PostgreSql`, `Postgres`, `Npgsql` | `PostgreSql` |
| `MySql` | `MySql` |

`Contoso.SqliteMigrations.Migrations` and `Contoso.Orders.Migrations.Sqlite` both resolve to SQLite.
A namespace with no recognisable segment is skipped silently, which shows up as a provider whose
migrations never run — if a migration appears to be ignored, check its namespace first.

### What does not need to change

- **`[DbContext(typeof(...))]` on existing `Designer.cs` and snapshot files.** These are only used
  to find the files; the type they name is ignored. Files generated against a shared context still
  work after the move.
- **Migration IDs.** They are grouped per provider, so the same ID under two providers is fine.
- **`.sql` script names** for the initial phase, provided the resource name they are journaled
  under is preserved — see [Adopting an existing journal](#adopting-an-existing-journal). Transition
  scripts are unjournaled, so theirs are free to change.

New migrations scaffolded after the move go under the generated provider subcontexts
(`SqliteContext`, `PostgreSqlContext`, …) and land in `Migrations/<Provider>/`, so a project can
hold both the old and new namespaces indefinitely.

### Providers are generated from package references

A provider subcontext, design-time factory, and switch case are emitted only for EF provider
packages the schema project actually references. Reference the same provider packages the original
projects did, or that provider silently disappears from the generated code.

### Replacing the old registration

Runtime wiring that selected a migrations assembly by name, such as:

```csharp
options.UseNpgsql(connectionString, b => b.MigrationsAssembly("PostgresMigrations"));
```

is replaced by the generated extension, which picks the provider from `DatabaseOptions.Provider`
and installs the generated migrations-assembly dispatcher:

```csharp
services.AddOrderDatabase();
```

### Verifying the move

Before pointing anything at a real database, confirm the generator found what you expect:

```bash
# Should list the migrations that moved in, per provider
dotnet ef migrations list --context SqliteContext

# Should report no pending scripts against an already-migrated database
dotnet run -- <connectionString> --dry-run

# Should list only what sits under Transition/
dotnet run -- <connectionString> --dry-run --phase Transition
```

A dry run that lists scripts you know are already applied means the journal names changed; a
`migrations list` that comes back empty means the namespaces did not resolve.

## Building a SQL Server migrator image

A deployment that only migrates SQL Server doesn't need EF Core tooling or the EF migration classes.
Publish with:

```bash
dotnet publish -p:SqlServerMigratorBuild=true
```

That trims the EF Core scaffolding from the generated code and leaves DbUp reading the embedded
scripts, so the image carries only what applying them needs. The resulting executable takes the SQL
Server flags below.

## Running migrations manually

The generated CLI accepts connections directly, which is useful in CI/CD pipelines:

```bash
# EF Core providers — one option per provider your project references, any number in one pass
dotnet run -- \
  --postgresql "Host=…;Database=orders" \
  --mysql "Server=…;Database=orders" \
  --sqlite "Data Source=orders.db"

# SQL Server (DbUp) — the connection string is positional
dotnet run -- "Server=…;Database=orders"
```

The option names are the providers your project references, lowercased: `--sqlite`, `--sqlserver`,
`--postgresql`, `--mysql`. A provider you leave out is skipped. The SQL Server migrator is a separate
build of the same project (see [Building a SQL Server migrator image](#building-a-sql-server-migrator-image)) and takes its
connection string as a positional argument rather than an option.

The EF providers take connection strings only. To see what is pending for one of them, use
`dotnet ef migrations list --context <Provider>Context`.

> `--ConnectionStrings:Sqlite=…` and friends are a different thing: the generated design-time
> factories read them so `dotnet ef` can reach a database. They are not accepted by the CLI above.

Additional flags for SQL Server:

| Flag | Description |
|------|-------------|
| `--phase <Initial\|Transition>` | Which script set to apply (default: `Initial`) |
| `--dry-run` | Print the scripts that would be applied without executing them |
| `--no-transaction` | Run without a wrapping transaction (required for some DDL) |

## Configuration reference

### `DatabaseOptions`

Resolved by named options key matching the `MigratorKey`. Configure with:
`services.Configure<DatabaseOptions>(key, ...)`.

| Property | Type | Description |
|----------|------|-------------|
| `Provider` | `DatabaseProvider` | `Sqlite`, `SqlServer`, `PostgreSql`, or `MySql` |
| `ConnectionString` | `string` | Provider-specific connection string |

### `SqlServerMigrationOptions`

Named per schema, keyed by `IDatabaseMigrator.Name`. SQL Server only, which is why these are here
rather than on `MigrateAsync` — nothing SQL Server specific is reachable from a provider-agnostic
call.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Phase` | `MigrationPhase` | `Initial` | Which script set to apply |
| `DryRun` | `bool` | `false` | Report the scripts a migration would apply, through the logger, and apply none |
| `ScriptPrefix` | `string` | *(none)* | Set for you from `BitSqlServerScriptPrefix`; override only when registering the runtime types by hand |
| `NoTransaction` | `bool` | `false` | Run without a wrapping transaction, and extend the per-script timeout from 5 minutes to 60 |
| `PreviousScriptPrefix` | `string?` | `null` | Set for you from `BitPreviousSqlServerScriptPrefix` — see [Rewriting recorded names](#rewriting-recorded-names) |

### `MigrationPhase`

| Value | Folder | Journal | Purpose |
|-------|--------|---------|---------|
| `Initial` | `<prefix>.` | Recorded | Schema changes that keep the deployed release working |
| `Transition` | `<prefix>.Transition.` | Not recorded | Data backfill while two releases are live; re-applies every run, so scripts must be idempotent |

`<prefix>` is `BitSqlServerScriptPrefix`. Name scripts with the usual date prefix:
a script called literally `Transition.sql` in the root folder would be taken for a transition script.

A phase is configuration rather than a per-call argument, so the startup runner refuses anything but
`Initial` — an unjournaled transition phase would otherwise re-apply on every boot.

### `AutoMigrateOptions`

Controls whether the hosted service applies a schema's migrations on startup. Named per schema, like
`DatabaseOptions`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AutoMigrate` | `bool` | `false` | Whether this host applies this schema on startup |

Off unless asked for, because migrating is an ownership decision: several services usually share a
schema, and each of them migrating it on boot means concurrent runs against the same database.
`AutoMigrateWhenSelfHosted` is the opt-in — see
[Migrating on startup](#4-migrating-on-startup). Configure the option directly for an unconditional
run, which is mostly useful in development:

```csharp
services.Configure<AutoMigrateOptions>("Vault", o => o.AutoMigrate = true);
```

These two are not interchangeable, and mixing them is order-sensitive: configuration actions run in
registration order and the last one wins, so `AutoMigrateWhenSelfHosted` registered afterwards
overwrites an unconditional `true` with whether the instance is self-hosted. Pick one per schema.

Because the options are named, `services.Configure<AutoMigrateOptions>(o => ...)` with no key
configures only the default name and has no effect on a registered schema.

## Advanced: running migrations on demand

`IDatabaseMigrator` is registered as a keyed scoped service. Resolve it by migrator key to apply
migrations programmatically:

```csharp
await using var scope = serviceProvider.CreateAsyncScope();
var migrator = scope.ServiceProvider.GetRequiredKeyedService<IDatabaseMigrator>("Order");
await migrator.MigrateAsync();
```

To run a phase other than the default, configure it for that schema first:

```csharp
services.Configure<SqlServerMigrationOptions>("Order", o => o.Phase = MigrationPhase.Transition);
```

## Project setup

`Bitwarden.Server.Sdk` will grow a `BitIncludeDatabase` property that does everything in this
section — including setting `OutputType` so the project builds as a migration executable:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Bitwarden.Server.Sdk" />

  <PropertyGroup>
    <BitIncludeDatabase>true</BitIncludeDatabase>
  </PropertyGroup>
</Project>
```

Until that ships — or on an SDK version without it — a schema project can set itself up. Everything
above works unchanged once this is in place, and the property names below are the ones the SDK will
set, so nothing has to be rewritten when it lands.

### Output type

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
</PropertyGroup>
```

The generated code includes the executable's entry point, so the project has to build as one.

### Packages

```xml
<ItemGroup>
  <PackageReference Include="Bitwarden.Server.Sdk.Database" Version="0.1.0" />
  <!-- The generated migration entry point is built on System.CommandLine. -->
  <PackageReference Include="System.CommandLine" Version="2.0.11" />
  <!-- Design-time tooling for `dotnet ef`, kept out of consumers' dependency graphs. -->
  <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.8" PrivateAssets="all" />
</ItemGroup>
```

The source generator ships inside this package and applies automatically through a
`PackageReference`, so nothing extra is needed for it.

### Properties the generator reads

```xml
<ItemGroup>
  <CompilerVisibleProperty Include="BitSqlServerScriptPrefix" />
  <CompilerVisibleProperty Include="BitPreviousSqlServerScriptPrefix" />
  <CompilerVisibleProperty Include="MSBuildProjectDirectory" />
</ItemGroup>
```

Without these the generated registration falls back to defaults: the schema's own namespace plus
`.SqlServer` as the prefix, no journal rewriting, and `add-migration` locating the project relative
to the output binary instead of by path.

### Script naming

Without the SDK, the script items are declared as well as named: the migrator finds scripts by
resource name, so the name has to be set rather than inherited from the file's location. A flat
script folder can do both inline:

```xml
<PropertyGroup>
  <BitSqlServerScriptPrefix>$(RootNamespace).SqlServer</BitSqlServerScriptPrefix>
</PropertyGroup>

<ItemGroup>
  <EmbeddedResource Include="Migrations\SqlServer\*.sql"
                    LogicalName="$(BitSqlServerScriptPrefix).%(Filename)%(Extension)" />
  <!-- Raw SQL an EF Core migration runs; these keep their default names. -->
  <EmbeddedResource Include="Migrations\**\HelperScripts\**\*.sql" />
</ItemGroup>
```

Phase and exclusion folders need more: `%(RecursiveDir)` keeps a directory separator, so
`Transition/x.sql` would become `Transition\x.sql` and the migrator wouldn't match it. Converting
separators takes a property function, which MSBuild only allows inside a target:

```xml
<ItemGroup>
  <EmbeddedResource Include="Migrations\SqlServer\**\*.sql" />
</ItemGroup>

<Target Name="NameDatabaseScripts" BeforeTargets="PrepareForBuild">
  <ItemGroup>
    <EmbeddedResource
      Condition="'%(Extension)' == '.sql' AND $([System.String]::Copy('%(Identity)').Replace('\', '/').StartsWith('Migrations/SqlServer/'))"
      LogicalName="$(BitSqlServerScriptPrefix).$([System.String]::Copy('%(RecursiveDir)').Replace('\', '.').Replace('/', '.'))%(Filename)%(Extension)" />
  </ItemGroup>
</Target>
```

The `Condition` matters: it scopes renaming to the script folder so SQL embedded for other reasons —
helper scripts, seed data — keeps its own name and is never picked up as a migration.

### Migrator builds

```xml
<PropertyGroup Condition="'$(SqlServerMigratorBuild)' == 'true'">
  <DefineConstants>$(DefineConstants);SQLSERVER_MIGRATOR_BUILD</DefineConstants>
</PropertyGroup>

<ItemGroup Condition="'$(SqlServerMigratorBuild)' == 'true'">
  <Compile Remove="Migrations\**" />
</ItemGroup>
```

That is what `dotnet publish -p:SqlServerMigratorBuild=true` needs to produce the image described in
[Building a SQL Server migrator image](#building-a-sql-server-migrator-image).

