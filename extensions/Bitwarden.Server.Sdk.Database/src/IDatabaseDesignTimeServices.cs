namespace Bitwarden.Server.Sdk.Database;

/// <summary>
/// Registers the services a schema's context needs beyond its options, so the generated design-time
/// factories can construct it for EF Core tooling. Named with
/// <see cref="DatabaseSetupAttribute{TContext}.DesignTimeServices"/>.
/// </summary>
/// <remarks>
/// Only design-time tooling, such as <c>add-migration</c>, uses these registrations. The application
/// resolves its context from its own service collection as usual.
/// </remarks>
public interface IDatabaseDesignTimeServices
{
    /// <summary>Adds the context's constructor dependencies to <see cref="DesignTimeContext.Services"/>.</summary>
    /// <param name="context">What the design-time factory is building.</param>
    static abstract void ConfigureServices(DesignTimeContext context);
}
