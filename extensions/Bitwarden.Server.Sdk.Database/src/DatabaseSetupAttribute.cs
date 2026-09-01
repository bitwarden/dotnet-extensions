using Microsoft.EntityFrameworkCore;

namespace Bitwarden.Server.Sdk.Database;

/// <summary>
/// Marks an assembly as hosting a database schema and triggers source generation of the
/// migration plumbing — provider subcontexts, design-time factories, the migrations-assembly
/// dispatcher, a DI extension method, and a CLI entry point.
/// </summary>
/// <remarks>
/// Only one <c>[assembly: DatabaseSetup&lt;TContext&gt;]</c> attribute is allowed per assembly
/// — applying it more than once is a compile-time error (BW0005) — so each schema needs its
/// own project.
/// </remarks>
/// <typeparam name="TContext">The root <see cref="DbContext"/> for this schema.</typeparam>
/// <example>
/// <code>
/// [assembly: DatabaseSetup&lt;MyDatabaseContext&gt;(MigratorKey = "MyDb")]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class DatabaseSetupAttribute<TContext> : Attribute
    where TContext : DbContext
{
    /// <summary>
    /// Named-options key used to resolve <see cref="DatabaseOptions"/> and the keyed
    /// <see cref="IDatabaseMigrator"/>. Defaults to the schema key derived from
    /// <typeparamref name="TContext"/> by stripping the <c>DatabaseContext</c> suffix
    /// (e.g., <c>VaultDatabaseContext</c> → <c>Vault</c>), falling back to stripping
    /// just <c>Context</c> for types not following the <c>*DatabaseContext</c> convention.
    /// </summary>
    public string? MigratorKey { get; set; }

    // TODO: Add a MigrationsNamespace property so the generator uses a caller-supplied namespace
    // for the generated provider subcontexts and design-time factories instead of the context
    // type's own namespace. This allows migration files that were authored in a separate assembly
    // (e.g. Bit.VaultDatabase.Migrations.Sqlite) to move into the schema project without their
    // namespaces being renamed, leaving the existing [DbContext(typeof(SqliteContext))] and
    // [Migration("...")] attributes valid. Without it, every migration file needs updating when
    // schemas are consolidated into a single project.

    // TODO: Add a MySqlServerVersion property (e.g. "8.0.21") so the generator can emit a
    // static ServerVersion instead of calling ServerVersion.AutoDetect(connectionString).
    // AutoDetect opens a live connection, which trips up design-time EF tooling (dotnet ef
    // migrations add) and DI registration when no MySQL server is reachable. It would need to
    // be a string rather than a Version, since attribute arguments are compile-time constants.
    // The generated MySqlContextFactory and the AddDatabase DI registration would both use
    // MySqlServerVersion.Parse(MySqlServerVersion) when the property is set, falling back to
    // AutoDetect only when it is null.
}
