using Microsoft.EntityFrameworkCore;

namespace Bitwarden.Server.Sdk.Database;

internal sealed class EfCoreMigrator<TContext> : IDatabaseMigrator
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
}
