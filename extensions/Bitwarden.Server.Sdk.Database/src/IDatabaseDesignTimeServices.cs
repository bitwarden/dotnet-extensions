namespace Bitwarden.Server.Sdk.Database;

/// <summary>
/// Registers the services a schema's context needs beyond its options, so the generated design-time
/// factories can construct it for EF Core tooling and the generated migration executable can resolve
/// it to migrate it. Named with <see cref="DatabaseSetupAttribute{TContext}.DesignTimeServices"/>.
/// </summary>
/// <remarks>
/// Design-time tooling, such as <c>add-migration</c>, and the migration executable use these
/// registrations, both to build the context for migrating, never to read its rows; the executable
/// migrates real databases with them. The application resolves its context from its own service
/// collection as usual.
/// </remarks>
public interface IDatabaseDesignTimeServices
{
    /// <summary>
    /// Adds the context's constructor dependencies to <see cref="DesignTimeContext.Services"/>.
    /// </summary>
    /// <param name="context">The services the context is resolved from.</param>
    static abstract void ConfigureServices(DesignTimeContext context);
}
