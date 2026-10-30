namespace Bitwarden.Server.Sdk.Database;

/// <summary>How a schema migrates SQL Server, set with <see cref="DatabaseSetupAttribute{TContext}.SqlServerMigrations"/>.</summary>
public enum SqlServerMigrations
{
    /// <summary>
    /// DbUp applies hand-authored scripts embedded under the schema's script prefix. The default.
    /// </summary>
    DbUp,

    /// <summary>
    /// EF Core migrations, scaffolded from the model and applied like every other engine's. For a
    /// schema whose SQL Server database holds nothing the model doesn't describe, such as stored
    /// procedures or views.
    /// </summary>
    EfCore,
}
