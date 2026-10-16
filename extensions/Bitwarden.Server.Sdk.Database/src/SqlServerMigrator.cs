using System.Data.Common;
using System.Reflection;
using DbUp;
using DbUp.Helpers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Database;

internal sealed class SqlServerMigrator : IDatabaseMigrator
{
    private readonly IOptionsMonitor<DatabaseOptions> _optionsMonitor;
    private readonly IOptionsMonitor<SqlServerMigrationOptions> _migrationOptionsMonitor;
    private readonly string _optionsName;
    private readonly Assembly _scriptsAssembly;
    private readonly ILogger<SqlServerMigrator> _logger;
    private readonly TimeProvider _timeProvider;

    public SqlServerMigrator(
        IOptionsMonitor<DatabaseOptions> optionsMonitor,
        IOptionsMonitor<SqlServerMigrationOptions> migrationOptionsMonitor,
        string optionsName,
        Assembly scriptsAssembly,
        ILogger<SqlServerMigrator> logger,
        TimeProvider timeProvider)
    {
        _optionsMonitor = optionsMonitor;
        _migrationOptionsMonitor = migrationOptionsMonitor;
        _optionsName = optionsName;
        _scriptsAssembly = scriptsAssembly;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    private const string JournalSchema = "dbo";
    private const string JournalTable = "Migration";

    public string Name => _optionsName;

    private string ConnectionString => _optionsMonitor.Get(_optionsName).ConnectionString;

    private SqlServerMigrationOptions MigrationOptions => _migrationOptionsMonitor.Get(_optionsName);

    /// <summary>
    /// The script set this migrator is configured to apply. Exposed for
    /// <see cref="DatabaseMigrationHostedService"/>, which applies only the initial phase.
    /// </summary>
    internal MigrationPhase Phase => MigrationOptions.Phase;

    /// <summary>
    /// Which embedded scripts a phase applies. <see cref="MigrationPhase.Initial"/> takes the
    /// scripts directly under the script folder, <see cref="MigrationPhase.Transition"/> takes the
    /// nested <c>Transition</c> folder, and archived scripts are excluded from both.
    /// </summary>
    internal static Func<string, bool> ScriptFilterFor(MigrationPhase phase, string scriptPrefix)
    {
        if (string.IsNullOrWhiteSpace(scriptPrefix))
        {
            throw new InvalidOperationException(
                "No script prefix is configured. The generated Add{Schema}Database sets it from the "
                + "BitSqlServerScriptPrefix build property; set SqlServerMigrationOptions.ScriptPrefix "
                + "when registering the runtime types by hand.");
        }

        var root = $"{scriptPrefix.TrimEnd('.')}.";
        var transition = $"{root}Transition.";
        var archive = $"{root}Archive.";

        return phase switch
        {
            // Transition and archived scripts start with the root prefix too, so the initial phase
            // has to exclude them explicitly or it would apply every set at once.
            MigrationPhase.Initial => name =>
                name.StartsWith(root, StringComparison.Ordinal)
                && !name.StartsWith(transition, StringComparison.Ordinal)
                && !name.StartsWith(archive, StringComparison.Ordinal),
            MigrationPhase.Transition => name =>
                name.StartsWith(transition, StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown migration phase."),
        };
    }

    /// <summary>
    /// Whether there is anything to rewrite. An unjournaled phase records nothing, so there is no
    /// journal for a rename to apply to.
    /// </summary>
    internal static bool ShouldRenameJournalEntries(SqlServerMigrationOptions options)
        => IsJournaled(options.Phase)
            && !string.IsNullOrWhiteSpace(options.PreviousScriptPrefix)
            && !string.IsNullOrWhiteSpace(options.ScriptPrefix)
            // Nothing to do when the journal already records what this build produces.
            && options.PreviousScriptPrefix!.TrimEnd('.') != options.ScriptPrefix.TrimEnd('.');

    /// <summary>
    /// Rewrites recorded script names in place. Values are parameterised; only the journal
    /// schema and table, which are fixed by this package, are part of the statement text.
    /// </summary>
    /// <remarks>
    /// Anchored to the start of the name, and skipping rows that already carry the new prefix, so
    /// running it twice is a no-op. An unanchored replace would rewrite a prefix appearing anywhere
    /// in the name, and would append the new segment again on every run whenever the new prefix
    /// extends the old one.
    /// </remarks>
    internal static string JournalRenameSql =>
        $"IF OBJECT_ID('{JournalSchema}.{JournalTable}','U') IS NOT NULL "
        + $"UPDATE [{JournalSchema}].[{JournalTable}] "
        + "SET [ScriptName] = STUFF([ScriptName], 1, LEN(@from), @to) "
        + "WHERE LEFT([ScriptName], LEN(@from)) = @from "
        + "AND LEFT([ScriptName], LEN(@to)) <> @to;";

    /// <summary>
    /// Whether a phase records what it applied. Transition scripts deliberately don't, so they
    /// re-apply on every invocation for as long as the transition window is open.
    /// </summary>
    internal static bool IsJournaled(MigrationPhase phase) => phase == MigrationPhase.Initial;

    /// <summary>
    /// Applies the configured phase, retrying while SQL Server reports it is in script upgrade
    /// mode — which it does for a while after patching, and which clears on its own.
    /// </summary>
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        var attempt = 1;
        while (true)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Preparing sits inside the retry, so a server in script upgrade mode is retried
                // whichever step hits it.
                await PrepareDatabaseAsync(cancellationToken);
                await MigrateCoreAsync(cancellationToken);
                return;
            }
            catch (DbException ex) when (ex.Message.Contains("Server is in script upgrade mode.") && attempt < 9)
            {
                _logger.LogInformation("Database is in script upgrade mode, retrying (attempt {Attempt}/9).", ++attempt);
                await Task.Delay(TimeSpan.FromSeconds(20), _timeProvider, cancellationToken);
            }
        }
    }

    private async Task PrepareDatabaseAsync(CancellationToken cancellationToken)
    {
        var connectionString = ConnectionString;

        var masterConnectionString = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master"
        }.ConnectionString;

        await using (var connection = new SqlConnection(masterConnectionString))
        {
            var databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
            if (string.IsNullOrWhiteSpace(databaseName))
                databaseName = "vault";

            var databaseNameQuoted = new SqlCommandBuilder().QuoteIdentifier(databaseName);

            await connection.OpenAsync(cancellationToken);

            var cmd = new SqlCommand(
                $"IF ((SELECT COUNT(1) FROM sys.databases WHERE [name] = @DatabaseName) = 0) " +
                $"CREATE DATABASE {databaseNameQuoted};", connection);
            cmd.Parameters.AddWithValue("@DatabaseName", databaseName);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            cmd.CommandText =
                $"IF ((SELECT DATABASEPROPERTYEX([name], 'IsAutoClose') " +
                $"FROM sys.databases WHERE [name] = @DatabaseName) = 1) " +
                $"ALTER DATABASE {databaseNameQuoted} SET AUTO_CLOSE OFF;";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await RenameJournalEntriesAsync(cancellationToken);
    }

    private async Task RenameJournalEntriesAsync(CancellationToken cancellationToken)
    {
        var options = MigrationOptions;
        if (!ShouldRenameJournalEntries(options))
            return;

        var previous = options.PreviousScriptPrefix!.TrimEnd('.');
        var current = options.ScriptPrefix.TrimEnd('.');

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        using var cmd = new SqlCommand(JournalRenameSql, connection);
        cmd.Parameters.AddWithValue("@from", $"{previous}.");
        cmd.Parameters.AddWithValue("@to", $"{current}.");

        var renamed = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (renamed > 0)
        {
            _logger.LogInformation(
                "Renamed {Count} journal entries from {Previous} to {Current}.",
                renamed,
                previous,
                current);
        }
    }

    private Task MigrateCoreAsync(CancellationToken cancellationToken)
    {
        var upgrader = BuildUpgrader();

        if (MigrationOptions.DryRun)
        {
            ReportPending(upgrader);
            return Task.CompletedTask;
        }

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException("SQL Server migration failed.", result.Error);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Logs what a migration would apply. Preparing the database has already happened by this
    /// point, as it would for a real run, so the report reflects the journal a migration would see.
    /// </summary>
    private void ReportPending(DbUp.Engine.UpgradeEngine upgrader)
    {
        var pending = upgrader.GetScriptsToExecute();

        _logger.LogInformation(
            "{Count} script(s) would be applied to {Name} in the {Phase} phase.",
            pending.Count,
            Name,
            MigrationOptions.Phase);

        foreach (var script in pending)
            _logger.LogInformation("Would apply {Script}.", script.Name);
    }

    private DbUp.Engine.UpgradeEngine BuildUpgrader()
    {
        var options = MigrationOptions;

        var builder = DeployChanges.To
            .SqlDatabase(ConnectionString)
            .WithScriptsEmbeddedInAssembly(
                _scriptsAssembly,
                ScriptFilterFor(options.Phase, options.ScriptPrefix))
            .WithExecutionTimeout(
                options.NoTransaction ? TimeSpan.FromMinutes(60) : TimeSpan.FromMinutes(5))
            .LogTo(new DbUpLogger(_logger));

        builder = options.NoTransaction ? builder.WithoutTransaction() : builder.WithTransaction();
        builder = IsJournaled(options.Phase)
            ? builder.JournalToSqlTable(JournalSchema, JournalTable)
            : builder.JournalTo(new NullJournal());

        return builder.Build();
    }
}
