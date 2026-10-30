using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Database;

/// <summary>
/// What a generated design-time factory hands to <see cref="IDatabaseDesignTimeServices.ConfigureServices"/>.
/// A class rather than bare parameters, so details such as the engine being scaffolded can be added
/// later without breaking implementations.
/// </summary>
public sealed class DesignTimeContext
{
    /// <summary>The design-time service collection the context's constructor is resolved from.</summary>
    public required IServiceCollection Services { get; init; }
}
