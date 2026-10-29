using Microsoft.EntityFrameworkCore;

namespace Bitwarden.Server.Sdk.Database;

/// <summary>
/// Marks an assembly as hosting a database schema and triggers source generation of the
/// migration plumbing — provider subcontexts, design-time factories, the migrations-assembly
/// dispatcher, a DI extension method, and a CLI entry point.
/// </summary>
/// <remarks>
/// Only one <c>[assembly: DatabaseSetup&lt;TContext&gt;]</c> attribute is allowed per assembly
/// — applying it more than once is a compile-time error (CS0579) — so each schema needs its
/// own project.
/// </remarks>
/// <typeparam name="TContext">The root <see cref="DbContext"/> for this schema.</typeparam>
/// <example>
/// <code>
/// [assembly: DatabaseSetup&lt;MyDatabaseContext&gt;(
///     [DatabaseProvider.SqlServer, DatabaseProvider.PostgreSql],
///     MigratorKey = "MyDb")]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class DatabaseSetupAttribute<TContext> : Attribute
    where TContext : DbContext
{
    /// <summary>Declares a schema and the database engines it supports.</summary>
    /// <param name="providers">The engines this schema supports; see <see cref="Providers"/>.</param>
    public DatabaseSetupAttribute(DatabaseProvider[] providers)
    {
        Providers = providers;
    }

    /// <summary>
    /// The database engines this schema supports. Code is generated for exactly these, and each
    /// one's provider package must be referenced. A provider package referenced for any other
    /// reason, such as tests or a transitive dependency, is ignored.
    /// </summary>
    public DatabaseProvider[] Providers { get; }

    /// <summary>
    /// Named-options key used to resolve <see cref="DatabaseOptions"/> and the keyed
    /// <see cref="IDatabaseMigrator"/>. Defaults to the schema key derived from
    /// <typeparamref name="TContext"/> by stripping the <c>DatabaseContext</c> suffix
    /// (e.g., <c>VaultDatabaseContext</c> → <c>Vault</c>), falling back to stripping
    /// just <c>Context</c> for types not following the <c>*DatabaseContext</c> convention.
    /// </summary>
    public string? MigratorKey { get; set; }

    // TODO (PM-45775): Add a MySqlServerVersion property (e.g. "8.0.36-mysql") so the generated
    // MySqlContextFactory can use ServerVersion.Parse instead of ServerVersion.AutoDetect, which
    // opens a live connection and stops design-time EF tooling (dotnet ef migrations add) working
    // when no MySQL server is reachable. It would need to be a string rather than a Version, since
    // attribute arguments are compile-time constants. The DI registration keeps AutoDetect: the
    // version decides the SQL generated against each deployment's MySQL or MariaDB server.
}
