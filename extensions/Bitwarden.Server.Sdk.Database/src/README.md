# Bitwarden.Server.Sdk.Database

Runtime support for the `DatabaseSetupGenerator` source generator. Apply
`[assembly: DatabaseSetup<TContext>]` to a schema project; the generator emits all migration
plumbing. See `PACKAGE.md` for the consumer guide.

## Public API surface

### Attribute (triggers code generation)

- `DatabaseSetupAttribute<TContext>` — assembly-level attribute. `MigratorKey` defaults to the
  schema name derived from `TContext` by stripping `DatabaseContext` (e.g. `VaultDatabaseContext` →
  `Vault`).

### Configuration

- `DatabaseOptions` — named options for a schema: `Provider` + `ConnectionString`. Resolved by the
  migrator key.
- `DatabaseProvider` — enum: `Sqlite`, `SqlServer`, `PostgreSql`, `MySql`.
- `AutoMigrateOptions` — named per schema, keyed by `IDatabaseMigrator.Name`; `AutoMigrate` is off
  unless a host opts in with `AutoMigrateWhenSelfHosted(key)`, since sharing a schema is normal and
  only its owner should apply it.

### DI extensions

- `DatabaseServiceCollectionExtensions.AddDatabase<TContext, TMigrationsAssembly>(name)` — core
  registration: configures the EF Core provider from named `DatabaseOptions`, replaces
  `IMigrationsAssembly` with the generated dispatcher, and registers a keyed `IDatabaseMigrator`.
  Registering a schema does not migrate it: the hosted runner only acts on schemas a host has opted
  in with `AutoMigrateWhenSelfHosted(key)`.
- `DatabaseServiceCollectionExtensions.AddSqlServerDatabaseMigrator(name, scriptsAssembly)` — SQL-Server-only
  variant: skips the EF Core DbContext and registers only the DbUp migrator.

### Migrators

- `IDatabaseMigrator` — keyed scoped service, one per registered schema. Call
  `MigrateAsync()` to apply migrations programmatically. `Name` returns the schema
  it belongs to, which is both its DI key and its named-options key — the DI container can't hand
  back the key a keyed service was registered under, so anything enumerating migrators (the hosted
  service, logging) reads it from here.
- `SqlServerMigrationOptions` — named per schema: `Phase`, `ScriptPrefix`, `DryRun`,
  `NoTransaction`, `PreviousScriptPrefix`. SQL Server only, so they are configuration rather than
  call arguments. `DryRun` reports through `ILogger` and keeps the migrator internal — nothing has
  to reach past `IDatabaseMigrator` to ask what is pending.
- `MigrationPhase` — `Initial` (journaled, scripts directly under the folder) or `Transition`
  (unjournaled, the nested `Transition` folder).
- `MigrationBuilderExtensions.SqlFromHelperScript` — runs a `.sql` file from
  `Migrations/{Provider}/HelperScripts/`, resolved against the migration's active provider.

### Base class for generated migrations-assembly

- `MigrationsAssemblyBase` — `[EditorBrowsable(Never)]` abstract base implementing EF Core's
  `IMigrationsAssembly`. Constructed with pre-built `(Id, TypeInfo, Func<Migration>)[]` entries and
  an optional `ModelSnapshot`; performs no runtime type scanning. The generator emits a
  `file`-scoped concrete subclass per schema.

### CLI helpers

- `DatabaseMigrationCli` — `[EditorBrowsable(Never)]` static class called by the generated
  `{Schema}MigrationProgram`. Not intended for direct use.

## What the source generator emits

For `[assembly: DatabaseSetup<VaultDatabaseContext>(MigratorKey = "Vault")]` the generator
produces a single source file `VaultDatabase.g.cs` containing (behind `#if !SQLSERVER_MIGRATOR_BUILD`
where relevant):

| Generated type | Scope | Purpose |
|---|---|---|
| `SqliteContext`, `PostgreSqlContext`, `MySqlContext` | `internal` | Provider subcontexts; `internal` so EF Designer files can reference them via `[DbContext(typeof(...))]`. No SQL Server subcontext — DbUp owns that schema |
| `SqliteContextFactory`, `PostgreSqlContextFactory`, `MySqlContextFactory` | `file` | EF design-time factories discovered by `dotnet ef` tooling |
| `VaultDatabaseMigrationsAssembly` | `file` | Concrete `MigrationsAssemblyBase` with compile-time migration lists; used by EF Core's `IMigrationsAssembly`. Dispatches on an exact `IDatabaseProvider.Name` match, and throws for SQL Server |
| `VaultDatabaseServiceCollectionExtensions` | `public` | `AddVaultDatabase()` extension method |
| `VaultDatabaseMigrationProgram` | `internal` | CLI entry point; called from `Program.Main` |
| `Program` | `internal` | `static Task Main(string[] args)` delegating to `VaultDatabaseMigrationProgram.RunAsync` |

## Compile-time symbols

| Symbol | Effect |
|--------|--------|
| `SQLSERVER_MIGRATOR_BUILD` | Strips EF Core provider contexts, factories, and the `IMigrationsAssembly` dispatcher. `AddVaultDatabase()` calls `AddSqlServerDatabaseMigrator` instead. Used by SQL-Server-only deployment builds that bundle only DbUp. |

## SQL Server script conventions

DbUp scripts are embedded resources whose `LogicalName` the SDK sets, defaulting to
`<RootNamespace>.SqlServer.<Filename>.sql` — `$(BitSqlServerScriptPrefix)`, then any nested folders,
then the file name. Name files with a sortable date prefix:

```
2026-08-12_00_InitialCreate.sql
2026-08-13_00_AddIndexOnOrderId.sql
```

The phase and exclusion folders sit inside the script folder, so `Transition/x.sql` becomes
`<namespace>.SqlServer.Transition.x.sql` and `Archive/x.sql` becomes
`<namespace>.SqlServer.Archive.x.sql`.

Scripts are journaled in `dbo.Migration` by embedded resource name, so applied scripts shouldn't be
renamed or deleted. A schema inheriting a journal from another assembly reproduces the recorded names
through the `LogicalName` on its `EmbeddedResource` items, so nothing in the journal needs
rewriting — see PACKAGE.md for the project setup. Transition scripts are exempt: that phase is unjournaled, so their names are
never recorded.

Where a single naming scheme can't match every recorded row — an instance upgrading from far enough
back that its journal predates the current one — `BitPreviousSqlServerScriptPrefix` names the prefix
those rows carry, and the recorded names are rewritten onto `BitSqlServerScriptPrefix`. The generated
`Add{Schema}Database` passes both through to `SqlServerMigrationOptions`. It is opt-in because it mutates deployment
history, which the other properties never do.

Author SQL scripts in a `.sqlproj` (SQL Server Data Tools) for IntelliSense and schema diffing —
the `.csproj` remains the build and embedding host.

## Migration file layout

```
MySchema/
  Migrations/
    Sqlite/ ← EF Core migration files (.cs, Designer.cs, Snapshot.cs)
    PostgreSql/ ← EF Core migration files
    MySql/ ← EF Core migration files (optional)
    Sqlite/HelperScripts/ ← raw .sql an EF migration runs
      2026-08-19_00_Backfill.sql
    SqlServer/ ← hand-authored .sql applied by DbUp
      2026-08-12_00_InitialCreate.sql
      Transition/ ← data backfills for the transition phase
        2026-08-19_00_BackfillEmail.sql
      Archive/ ← retired scripts, excluded from every phase
        2020-01-01_00_Old.sql
```

One glob covers all of it, because the directory nesting is what selects the phase:

```xml
<EmbeddedResource Include="Migrations\SqlServer\**\*.sql" />
```

`Transition/x.sql` becomes `<namespace>.SqlServer.Transition.x.sql`, and the initial phase excludes
that segment explicitly — a transition script's name contains the root segment too, so without the
exclusion both sets would apply together. `Archive/` is excluded from both phases. Since a
transition run is unjournaled, those names are never recorded and are free to change; only the
initial phase's names have to stay stable.

SQL Server has no EF Core migrations, and the generated code closes both routes to pretending
otherwise. No subcontext or design-time factory is emitted, so there is no `SqlServerContext` for
`dotnet ef` to target and nothing to scaffold into `Migrations/SqlServer/`. The migrations-assembly
dispatcher throws for a SQL Server-configured context rather than reporting an empty set, so
`DbContext.Database.Migrate()` fails with a message pointing at the migrator instead of quietly
creating `__EFMigrationsHistory` and applying nothing. The DI switch still configures
`UseSqlServer` for the root context, since querying SQL Server through EF is unaffected.

## Running the tests

The in-process tests run in well under a second:

```bash
dotnet run --project tests/Bitwarden.Server.Sdk.Database.Tests
```

Tests that need a real database — DbUp applying scripts, the journal and its phase behaviour,
adopting a journal written under an older namespace, and the PostgreSQL and MySQL providers — are
marked explicit so they stay out of that loop. They start containers on first use, so a default run
starts none:

```bash
dotnet run --project tests/Bitwarden.Server.Sdk.Database.Tests -- --explicit On
```

Roughly 12 seconds with the images cached. CI should run this variant: explicit tests are easy to
forget, and these cover the paths that touch a live database.

## SDK integration

The MSBuild side of this — one `BitIncludeDatabase` property setting up the package references,
compiler-visible properties, script naming, and the migrator-build switch — is written but parked
in a stash (`Server.Sdk database integration`), waiting on `Bitwarden.Server.Sdk` shipping with the
analyzer support it depends on. Until then PACKAGE.md documents the same setup as project
configuration a consumer writes themselves, using the property names the SDK will set, so nothing
has to change when it lands.

## Migration phases

Evolutionary database design splits a schema change across three migrations. They are separate
invocations spread over a release cycle rather than steps in one run, which is what the design has
to accommodate:

| Phase | When it runs | What belongs in it |
|-------|--------------|--------------------|
| Initial | Ahead of the deployment of release X+1 | Schema changes that keep release X working; must be quick |
| Transition | During the window where X and X+1 are both live | Optional data backfill only — batched, no schema changes |
| Finalization | With the deployment of a later release | Removal of the compatibility scaffolding |

Nothing sequences them: initial goes out with a deployment, transition is triggered as a background
job when it is needed, and finalization ships with a release two ahead. The runner only ever
performs one phase per invocation.

How the phases are actually driven matters more than the phase names:

- **Initial** is the ordinary migration run — the default script folder, journaled.
- **Transition** is run against its own folder with the journal disabled, so it re-applies on every
  invocation. That is what makes it safe to trigger repeatedly during the transition window, and it
  means transition scripts must be idempotent by construction.
- **Finalization** is never executed from its own folder. It is a staging area in the source tree
  that a release-time job folds into the main folder, renaming each script with a date prefix. From
  the runner's point of view a finalization script is simply a new initial-phase script.

Implemented:

- `MigrationPhase` on `SqlServerMigrationOptions`, named per schema. `Initial` reads the configured
  folder and journals; `Transition` reads the nested `Transition` folder and skips the journal — the
  pairing the deployment pipeline previously spelled as `-f DbScripts_transition -r`.
- `--phase` on the generated SQL Server CLI, replacing the coupled `-f`/`-r` pair. `Repeatable` and
  `FolderName` are gone: the phase expresses both, and neither was used independently.
- The startup runner refuses any phase but `Initial`.
- Scripts under `Migrations/SqlServer` and under any `HelperScripts` folder are embedded by
  convention, with `Exclude="@(EmbeddedResource)"` so a project declaring its own items keeps them.
  The script folder is fixed: `BitSqlServerScriptPrefix` describes the recorded name, not the
  layout, so an adopted schema moves its scripts like any other.
- `BitSqlServerScriptPrefix` and `BitPreviousSqlServerScriptPrefix` are read from `CompilerVisibleProperty`
  items, and the generated `Add{Schema}Database` bakes them into `SqlServerMigrationOptions` — so the folder scripts are embedded under and the folder
  the migrator reads come from one property. The rename is unconditional for database projects,
  which is what makes that guarantee hold.

Still open:

- Finalization needs no runner. The release-cut workflow folds those scripts into the main folder,
  where they arrive as ordinary initial-phase scripts.
- Finalization scripts arriving via the release-cut fold-in are renamed, so they re-apply under
  their new names. That is the intended behaviour, and the reason phase scripts must be idempotent.

Folding a phase folder into the main folder renames its scripts, so the journal treats them as new
and applies them again. That is the intended behaviour rather than a flaw, and it is the second
reason phase scripts have to be idempotent.

EF Core providers stay phase-less. The phases exist for rolling deployments, which the EF-backed
providers don't serve, and supporting them would mean filtering the entries the generated
migrations-assembly hands to EF — EF applies whatever migrations it can see, so the phase would have
to be resolved from `DbContextOptions` when the dispatcher is constructed.

