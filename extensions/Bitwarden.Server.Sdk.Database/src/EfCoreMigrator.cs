using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Database;

internal sealed class EfCoreMigrator<TContext> : IDatabaseMigrator, IDatabaseAvailability
    where TContext : DbContext
{
    private readonly string _name;
    private readonly IServiceProvider _services;
    private TContext? _context;

    public EfCoreMigrator(string name, IServiceProvider services)
    {
        _name = name;
        _services = services;
    }

    public string Name => _name;

    /// <summary>
    /// Resolved on first use rather than with the migrator. Configuring a context can connect, as
    /// MySQL's ServerVersion.AutoDetect does, so resolving every migrator at startup would connect to
    /// every database before the startup wait, including ones the host never migrates.
    /// </summary>
    private TContext Context => _context ??= _services.GetRequiredService<TContext>();

    public Task MigrateAsync(CancellationToken cancellationToken = default)
        => Context.Database.MigrateAsync(cancellationToken);

    /// <summary>
    /// Asks the provider whether the database exists, which needs the server to answer but not the
    /// database to be there yet.
    /// </summary>
    public async Task<DbException?> CheckConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Context.GetService<IRelationalDatabaseCreator>().ExistsAsync(cancellationToken);
            return null;
        }
        catch (DbException e)
        {
            return e;
        }
    }
}
