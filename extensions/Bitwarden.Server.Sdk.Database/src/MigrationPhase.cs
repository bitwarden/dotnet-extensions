using System.ComponentModel;

namespace Bitwarden.Server.Sdk.Database;

/// <summary>
/// Which set of SQL Server scripts a migrator applies, per evolutionary database design.
/// </summary>
public enum MigrationPhase
{
    /// <summary>
    /// Schema changes that keep the currently deployed release working, applied from the main
    /// script folder and recorded in the journal so each script runs once.
    /// </summary>
    Initial = 0,

    /// <summary>
    /// An optional data backfill, applied from the nested <c>Transition</c> script folder while two
    /// releases are live. Runs without a journal, so every script re-applies on each invocation
    /// and transition scripts have to be idempotent.
    /// </summary>
    /// <remarks>
    /// You should not need to select this yourself. It exists for the generated migrator utility,
    /// which sets it from its <c>--phase</c> option, and it is a deployment step rather than
    /// something a host applies at startup — <see cref="DatabaseMigrationHostedService"/> skips any
    /// schema configured with it.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    Transition = 1,
}
