namespace Bitwarden.Server.Sdk.Database;

/// <summary>Applies pending migrations to a database schema.</summary>
public interface IDatabaseMigrator
{
    /// <summary>
    /// The schema this migrator applies migrations to, which is both its keyed-service key and
    /// the named-options key its <see cref="DatabaseOptions"/> are resolved from (e.g. <c>"Vault"</c>).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Applies any pending migrations to the database, creating it if necessary.
    /// </summary>
    Task MigrateAsync(CancellationToken cancellationToken = default);
}
