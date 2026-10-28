using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bitwarden.Server.Sdk.Database;

internal sealed class EfCoreMigrator<TContext> : IDatabaseMigrator, IDatabaseAvailability
    where TContext : DbContext
{
    private readonly string _name;
    private readonly TContext _context;

    public EfCoreMigrator(string name, TContext context)
    {
        _name = name;
        _context = context;
    }

    public string Name => _name;

    public Task MigrateAsync(CancellationToken cancellationToken = default)
        => _context.Database.MigrateAsync(cancellationToken);

    /// <summary>
    /// Asks the provider whether the database exists, which needs the server to answer but not the
    /// database to be there yet.
    /// </summary>
    public async Task<DbException?> CheckConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.GetService<IRelationalDatabaseCreator>().ExistsAsync(cancellationToken);
            return null;
        }
        catch (DbException e)
        {
            return e;
        }
    }
}
