using System.Data.Common;
using System.Reflection;
using System.Runtime.ExceptionServices;
using DbUp;
using DbUp.Helpers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Database;

internal sealed class SqlServerMigrator : IDatabaseMigrator, IDatabaseAvailability
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
    private const string MigrationLockResource = "Bitwarden.Server.Sdk.Database.Migration";

    // Prefix of the lock, taken in master, that serializes hosts creating the same database.
    private const string CreateLockResourcePrefix = "Bitwarden.Server.Sdk.Database.Create.";

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
            && options.PreviousScriptPrefix!.TrimEnd('.') != options.ScriptPrefix.TrimEnd('.')
            && NotOverlapping(
                options.PreviousScriptPrefix!.TrimEnd('.'), options.ScriptPrefix.TrimEnd('.'));

    /// <summary>
    /// Whether the rewrite can be applied safely. Overlapping prefixes are rejected at build time
    /// by <c>BWDB001</c>, so this only catches options configured by hand, and throws rather than
    /// skipping: a rewrite that silently does nothing leaves the journal under a name no script has
    /// and re-applies the whole set.
    /// </summary>
    private static bool NotOverlapping(string previous, string current)
    {
        if (previous.StartsWith(current, StringComparison.Ordinal)
            || current.StartsWith(previous, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"PreviousScriptPrefix '{previous}' overlaps ScriptPrefix '{current}': one is a "
                + "prefix of the other. The journal rewrite only supports adopting an unrelated "
                + "name.");
        }

        return true;
    }

    /// <summary>
    /// Rewrites recorded script names in place. Values are parameterised; only the journal
    /// schema and table, which are fixed by this package, are part of the statement text.
    /// </summary>
    /// <remarks>
    /// Anchored to the start of the name, and running it twice is a no-op: afterwards no row starts
    /// with the old prefix. That holds because the prefixes cannot overlap — <c>BWDB001</c> rejects
    /// a previous prefix that contains the current one or is contained by it, so the rewritten name
    /// can never match the pattern that produced it.
    /// </remarks>
    internal static string JournalRenameSql =>
        $"IF OBJECT_ID('{JournalSchema}.{JournalTable}','U') IS NOT NULL "
        + $"UPDATE [{JournalSchema}].[{JournalTable}] "
        + "SET [ScriptName] = STUFF([ScriptName], 1, LEN(@from), @to) "
        + "WHERE LEFT([ScriptName], LEN(@from)) = @from;";

    /// <summary>
    /// Whether a phase records what it applied. Transition scripts deliberately don't, so they
    /// re-apply on every invocation for as long as the transition window is open.
    /// </summary>
    internal static bool IsJournaled(MigrationPhase phase) => phase == MigrationPhase.Initial;

    /// <summary>
    /// Defaults the database to the lower-cased registration name when the connection string
    /// doesn't name one.
    /// </summary>
    internal static (string ConnectionString, string DatabaseName) ResolveDatabase(
        string connectionString,
        string optionsName)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
            builder.InitialCatalog = optionsName.ToLowerInvariant();

        return (builder.ConnectionString, builder.InitialCatalog);
    }

    /// <summary>
    /// Connects to <c>master</c>, which needs the server to be up but not this schema's database,
    /// since migrating creates it.
    /// </summary>
    public async Task<DbException?> CheckConnectionAsync(CancellationToken cancellationToken)
    {
        var master = new SqlConnectionStringBuilder(ResolveDatabase(ConnectionString, _optionsName).ConnectionString)
        {
            InitialCatalog = "master",
        }.ConnectionString;

        try
        {
            await using var connection = new SqlConnection(master);
            await connection.OpenAsync(cancellationToken);
            return null;
        }
        catch (DbException e)
        {
            return e;
        }
    }

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

                var (connectionString, databaseName) = ResolveDatabase(ConnectionString, _optionsName);

                // Preparing sits inside the retry, so a server in script upgrade mode is retried
                // whichever step hits it.
                await PrepareDatabaseAsync(connectionString, databaseName, cancellationToken);

                // Held from reading the journal to the last script, so a host migrating at the same
                // time waits, then finds the scripts already journaled.
                await using (await AcquireMigrationLockAsync(connectionString, MigrationLockResource, cancellationToken))
                {
                    await RenameJournalEntriesAsync(connectionString, cancellationToken);
                    await MigrateCoreAsync(connectionString);
                }

                return;
            }
            catch (DbException ex) when (ex.Message.Contains("Server is in script upgrade mode.") && attempt < 9)
            {
                _logger.LogInformation("Database is in script upgrade mode, retrying (attempt {Attempt}/9).", ++attempt);
                await Task.Delay(TimeSpan.FromSeconds(20), _timeProvider, cancellationToken);
            }
        }
    }

    private async Task PrepareDatabaseAsync(
        string connectionString,
        string databaseName,
        CancellationToken cancellationToken)
    {
        var masterConnectionString = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master"
        }.ConnectionString;

        // Held in master around the check and the create, since the migration lock lives in the
        // database being created. Without it, hosts starting together on a first deployment can
        // all find the database missing, and SQL Server severs the connection of each one whose
        // CREATE DATABASE loses the race.
        await using (await AcquireMigrationLockAsync(
            masterConnectionString, CreateLockResourcePrefix + databaseName, cancellationToken))
        await using (var connection = new SqlConnection(masterConnectionString))
        {
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
    }

    /// <summary>
    /// Takes an exclusive application lock on <paramref name="resource"/> in the connection's
    /// database, owned by a connection of its own so DbUp's sessions don't affect it. DbUp, unlike EF
    /// Core, doesn't lock, so two hosts would otherwise read the same pending scripts from the
    /// journal and both apply them. Also taken in <c>master</c> around creating the database.
    /// </summary>
    private static async Task<MigrationLock> AcquireMigrationLockAsync(
        string connectionString, string resource, CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);

            using var cmd = new SqlCommand(
                "DECLARE @result INT; "
                + "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', "
                + "@LockOwner = 'Session', @LockTimeout = -1; "
                + "SELECT @result;",
                connection);
            cmd.Parameters.AddWithValue("@resource", resource);
            cmd.CommandTimeout = 0;

            // 0 is granted at once, 1 after waiting; anything negative is a failure.
            var result = (int)(await cmd.ExecuteScalarAsync(cancellationToken))!;
            if (result < 0)
                throw new InvalidOperationException($"Could not take the migration lock (sp_getapplock returned {result}).");

            return new MigrationLock(connection, resource);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Releases the lock explicitly before disposing its connection. Disposing alone returns a
    /// pooled connection to the pool with its session, and the lock, still alive until the pool
    /// reuses it, so a waiting host would wait indefinitely.
    /// </summary>
    private sealed class MigrationLock : IAsyncDisposable
    {
        private readonly SqlConnection _connection;
        private readonly string _resource;

        public MigrationLock(SqlConnection connection, string resource)
        {
            _connection = connection;
            _resource = resource;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                using var cmd = new SqlCommand(
                    "EXEC sp_releaseapplock @Resource = @resource, @LockOwner = 'Session';", _connection);
                cmd.Parameters.AddWithValue("@resource", _resource);
                await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                await _connection.DisposeAsync();
            }
        }
    }

    private async Task RenameJournalEntriesAsync(string connectionString, CancellationToken cancellationToken)
    {
        var options = MigrationOptions;
        if (!ShouldRenameJournalEntries(options))
            return;

        var previous = options.PreviousScriptPrefix!.TrimEnd('.');
        var current = options.ScriptPrefix.TrimEnd('.');

        await using var connection = new SqlConnection(connectionString);
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

    private Task MigrateCoreAsync(string connectionString)
    {
        var upgrader = BuildUpgrader(connectionString);

        if (MigrationOptions.DryRun)
        {
            ReportPending(upgrader);
            return Task.CompletedTask;
        }

        // DbUp returns failures rather than throwing them. A database error is rethrown as-is so
        // the script upgrade mode retry can see it.
        var result = upgrader.PerformUpgrade();
        if (result.Error is DbException dbException)
            ExceptionDispatchInfo.Capture(dbException).Throw();
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

    private DbUp.Engine.UpgradeEngine BuildUpgrader(string connectionString)
    {
        var options = MigrationOptions;

        var builder = DeployChanges.To
            .SqlDatabase(connectionString)
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
