namespace Bitwarden.Server.Sdk.Database;

/// <summary>
/// Controls the behaviour of the hosted migration runner registered by
/// <see cref="DatabaseServiceCollectionExtensions.AddDatabase{TContext,TMigrationsAssembly}"/>.
/// </summary>
public sealed class AutoMigrateOptions
{
    /// <summary>
    /// Whether the hosted service should apply pending migrations for this schema on startup.
    /// </summary>
    /// <remarks>
    /// Off unless a host asks for it, because migrating is an ownership decision: several services
    /// usually share a schema and only one of them should apply it. Turn it on for a schema this
    /// host owns with
    /// <see cref="DatabaseServiceCollectionExtensions.AutoMigrateWhenSelfHosted"/>, or by
    /// configuring this option directly for an unconditional run.
    /// </remarks>
    public bool AutoMigrate { get; set; }
}
