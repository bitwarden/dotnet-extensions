using System.ComponentModel;

namespace Bitwarden.Server.Sdk.Database;

/// <summary>
/// SQL Server specific migration settings, consumed via named options — register with
/// <c>services.Configure&lt;SqlServerMigrationOptions&gt;(key, ...)</c> where <c>key</c> matches
/// <see cref="IDatabaseMigrator.Name"/>.
/// </summary>
/// <remarks>
/// These live here rather than on <see cref="IDatabaseMigrator.MigrateAsync"/> so that nothing
/// SQL Server specific is reachable from a provider-agnostic call, where the other providers
/// would have to either honour it or quietly ignore it.
/// </remarks>
public sealed class SqlServerMigrationOptions
{
    /// <summary>
    /// The set of scripts to apply. Defaults to <see cref="MigrationPhase.Initial"/>, which is the
    /// only phase a host applies at startup.
    /// </summary>
    /// <remarks>
    /// You should not need to configure this yourself. It exists for the generated migrator
    /// utility, which sets it from its <c>--phase</c> option on its own service collection;
    /// anything other than <see cref="MigrationPhase.Initial"/> describes a deployment step rather
    /// than a host's own startup, and <see cref="DatabaseMigrationHostedService"/> skips a schema
    /// configured with one.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public MigrationPhase Phase { get; set; } = MigrationPhase.Initial;

    /// <summary>
    /// Resource-name prefix the scripts are embedded under, and the name the journal records them
    /// by — everything ahead of the file name, e.g. <c>"Acme.Orders.SqlServer"</c>.
    /// <see cref="MigrationPhase.Transition"/> reads the <c>Transition</c> folder within it.
    /// </summary>
    /// <remarks>
    /// Set for you by the generated <c>Add{Schema}Database</c> from the
    /// <c>BitSqlServerScriptPrefix</c> build property, so the name scripts are embedded under and
    /// the name the migrator looks for can't drift. Override only when registering the runtime
    /// types by hand.
    /// </remarks>
    public string ScriptPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Reports the scripts a migration would apply, through the logger, and applies none of them.
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Runs scripts without a wrapping transaction, which some DDL requires, and extends the
    /// per-script execution timeout from 5 minutes to 60.
    /// </summary>
    public bool NoTransaction { get; set; }

    /// <summary>
    /// Prefix a database's journal may still record scripts under, from before they moved — for
    /// example <c>"Bit.Setup.DbScripts"</c>. Recorded names carrying it are rewritten onto
    /// <see cref="ScriptPrefix"/> before a journaled phase migrates.
    /// </summary>
    /// <remarks>
    /// Set for you from the <c>BitPreviousSqlServerScriptPrefix</c> build property; override only
    /// when registering the runtime types by hand. Prefer matching the recorded name with
    /// <see cref="ScriptPrefix"/>, which leaves the journal alone — this is the escape hatch for
    /// what that can't cover: a database that may hold names from an older era as well, where no
    /// single prefix matches every row. The rewrite runs as part of preparing the database — so a
    /// dry run performs it too, matching the migrator this replaces — and is a no-op once applied.
    /// </remarks>
    public string? PreviousScriptPrefix { get; set; }
}
