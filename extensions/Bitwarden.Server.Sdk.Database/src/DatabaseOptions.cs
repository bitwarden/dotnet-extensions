namespace Bitwarden.Server.Sdk.Database;

/// <summary>
/// Configuration for a single database schema's connection. Consumed via named options —
/// register with <c>services.Configure&lt;DatabaseOptions&gt;(key, ...)</c> where <c>key</c>
/// matches the migrator key passed to <see cref="DatabaseServiceCollectionExtensions.AddDatabase{TContext,TMigrationsAssembly}"/>.
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>The database engine to connect to.</summary>
    public DatabaseProvider Provider { get; set; }

    /// <summary>The connection string for the target database.</summary>
    public string ConnectionString { get; set; } = string.Empty;
}
