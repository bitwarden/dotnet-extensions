using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Bitwarden.Server.Sdk.Database;

/// <summary>
/// Describes database errors for logs. Database messages can carry row values, such as the key in
/// a duplicate key error, so only the error's identifying numbers are logged; the exception itself
/// is still thrown to whoever ran the migration.
/// </summary>
internal static class DatabaseErrors
{
    public static string Describe(DbException exception) => exception switch
    {
        SqlException sql => $"SQL Server error {sql.Number} (class {sql.Class}, state {sql.State})",
        _ => $"{exception.GetType().Name} (error code {exception.ErrorCode}, SQL state {exception.SqlState ?? "none"})",
    };
}
