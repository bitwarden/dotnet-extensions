using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Database;

/// <summary>
/// What a generated design-time factory, or the generated migration executable, hands to
/// <see cref="IDatabaseDesignTimeServices.ConfigureServices"/>. A class rather than bare parameters, so
/// details such as the engine being scaffolded can be added later without breaking implementations.
/// </summary>
public sealed class DesignTimeContext
{
    /// <summary>
    /// The service collection the context's constructor is resolved from: the design-time factory's own,
    /// or the migration executable's, which also holds the schema's registrations.
    /// </summary>
    public required IServiceCollection Services { get; init; }
}
