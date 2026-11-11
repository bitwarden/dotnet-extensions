using System.Data.Common;

namespace Bitwarden.Server.Sdk.Database;

/// <summary>
/// Checks whether a migrator's database server accepts connections, so startup can wait for it
/// before migrating once, rather than retrying the migration itself.
/// </summary>
internal interface IDatabaseAvailability
{
    /// <summary>
    /// Returns <see langword="null"/> when the server accepts a connection, or the error that
    /// stopped it. A database that doesn't exist yet still counts as available: migrating creates it.
    /// </summary>
    Task<DbException?> CheckConnectionAsync(CancellationToken cancellationToken);
}
