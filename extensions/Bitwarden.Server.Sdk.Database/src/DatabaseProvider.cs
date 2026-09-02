namespace Bitwarden.Server.Sdk.Database;

/// <summary>Identifies the database engine used by a schema's migration pipeline.</summary>
public enum DatabaseProvider
{
    /// <summary>SQLite — lightweight file-based or in-memory database. Managed via EF Core migrations.</summary>
    Sqlite,

    /// <summary>Microsoft SQL Server — managed via DbUp with embedded SQL scripts.</summary>
    SqlServer,

    /// <summary>PostgreSQL — managed via EF Core migrations using the Npgsql provider.</summary>
    PostgreSql,

    /// <summary>MySQL / MariaDB — managed via EF Core migrations using the Pomelo provider.</summary>
    MySql,
}
