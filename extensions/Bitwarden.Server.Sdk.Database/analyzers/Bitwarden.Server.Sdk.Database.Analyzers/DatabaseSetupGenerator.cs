using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Bitwarden.Server.Sdk.Database.Analyzers;

[Generator]
public sealed partial class DatabaseSetupGenerator : IIncrementalGenerator
{
    // Supported EF Core providers, in the order they get emitted. Name is used for generated class
    // names (e.g. SqliteContext) and switch cases; Assembly is both the assembly looked for in
    // ReferencedAssemblyNames to detect the provider and the value IDatabaseProvider.Name reports
    // at runtime, so one string serves compile-time detection and runtime dispatch.
    private static readonly ProviderSpec[] AllProviders =
    [
        new("Sqlite", "Microsoft.EntityFrameworkCore.Sqlite"),
        new("SqlServer", "Microsoft.EntityFrameworkCore.SqlServer"),
        new("PostgreSql", "Npgsql.EntityFrameworkCore.PostgreSQL"),
        new("MySql", "Pomelo.EntityFrameworkCore.MySql"),
    ];

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Each step below produces an equatable output type so Roslyn's incremental caching
        // layer can skip downstream generation when that slice hasn't changed, even if an
        // unrelated part of the compilation was edited.

        // Build properties the SDK makes compiler-visible; these change on project reload.
        var buildPropertiesProvider = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
            {
                options.GlobalOptions.TryGetValue("build_property.MSBuildProjectDirectory", out var dir);
                options.GlobalOptions.TryGetValue("build_property.BitSqlServerScriptPrefix", out var prefix);
                options.GlobalOptions.TryGetValue("build_property.BitPreviousSqlServerScriptPrefix", out var previous);
                return new BuildProperties(
                    dir,
                    string.IsNullOrWhiteSpace(prefix) ? null : prefix!.TrimEnd('.'),
                    string.IsNullOrWhiteSpace(previous) ? null : previous!.TrimEnd('.'));
            });

        // Referenced EF Core providers, which change when project references do.
        var providersProvider = context.CompilationProvider
            .Select(static (c, _) => DetectProviders(c));

        // Assembly-level DatabaseSetup<TContext> attributes. ForAttributeWithMetadataName is
        // more targeted than CompilationProvider.Select, since it reruns when the attribute
        // syntax or the referenced context type changes rather than on every compilation edit.
        // For assembly attributes the predicate node is the CompilationUnitSyntax of the file
        // declaring [assembly: DatabaseSetup<T>]. AllowMultiple is false, so there's at most
        // one per project and this values provider runs 0 or 1 times; a .Collect() here would
        // just add a layer to iterate through.
        var attrProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Bitwarden.Server.Sdk.Database.DatabaseSetupAttribute`1",
                predicate: static (node, _) => node is CompilationUnitSyntax,
                transform: static (ctx, _) => ExtractAttributeInfo(ctx.Attributes[0]));

        // Migrations: fires when a class bearing [Migration("...")] changes rather than on
        // every compilation edit. Each transform call returns one entry or null.
        var rawMigrationsProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Microsoft.EntityFrameworkCore.Migrations.MigrationAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => TryExtractMigration(ctx))
            .Where(static m => m.HasValue)
            .Select(static (m, _) => m!.Value)
            .Collect();

        // Snapshots: fires when a class bearing [DbContext(...)] changes. The transform checks
        // for ModelSnapshot inheritance and returns null for non-snapshot classes.
        var rawSnapshotsProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Microsoft.EntityFrameworkCore.Infrastructure.DbContextAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => TryExtractSnapshot(ctx))
            .Where(static s => s.HasValue)
            .Select(static (s, _) => s!.Value)
            .Collect();

        // Group the flat lists into provider-keyed MigrationData.
        var migrationDataProvider = rawMigrationsProvider
            .Combine(rawSnapshotsProvider)
            .Select(static (pair, _) => AssembleMigrationData(pair.Left, pair.Right));

        context.RegisterSourceOutput(
            attrProvider.Combine(providersProvider.Combine(migrationDataProvider).Combine(buildPropertiesProvider)),
            static (spc, data) =>
            {
                var (attr, ((providers, migrationData), buildProperties)) = data;
                GenerateForAttribute(spc, attr, migrationData, providers, buildProperties);
            });
    }

    // Provider detection

    /// <summary>
    /// Returns the subset of <see cref="AllProviders"/> whose package assemblies are present
    /// in the compilation's referenced assembly list. Only referenced providers get
    /// subcontexts, design-time factories, and switch cases generated for them.
    /// </summary>
    private static EquatableArray<ProviderSpec> DetectProviders(Compilation compilation)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in compilation.ReferencedAssemblyNames)
            referenced.Add(a.Name);

        var result = ImmutableArray.CreateBuilder<ProviderSpec>();
        foreach (var p in AllProviders)
        {
            if (referenced.Contains(p.Assembly))
                result.Add(p);
        }
        return new EquatableArray<ProviderSpec>(result.ToImmutable());
    }

    // Attribute info extraction

    // The ForAttributeWithMetadataName transform has already narrowed this down to a
    // DatabaseSetupAttribute`1 with its one type argument resolved.
    private static AttributeInfo ExtractAttributeInfo(AttributeData attr)
    {
        var typeArgs = attr.AttributeClass!.TypeArguments;
        var contextType = (INamedTypeSymbol)typeArgs[0];

        var ns = contextType.ContainingNamespace.ToDisplayString();
        var typeName = contextType.Name;

        // Schema name: strip trailing "Context"
        var schemaName = typeName.EndsWith("Context")
            ? typeName.Substring(0, typeName.Length - 7)
            : typeName;

        // Migrator key: strip trailing "Database" from schema name if present,
        // e.g. VaultDatabaseContext → schemaName=VaultDatabase → key=Vault
        var defaultKey = schemaName.EndsWith("Database")
            ? schemaName.Substring(0, schemaName.Length - 8)
            : schemaName;

        var namedArg = attr.NamedArguments.FirstOrDefault(kv => kv.Key == "MigratorKey");
        var migratorKey = namedArg.Value.Value as string ?? defaultKey;

        return new AttributeInfo(ns, typeName, schemaName, migratorKey);
    }

    // ForAttributeWithMetadataName transforms

    // Called for every class bearing [Migration("...")]. Returns null for abstract classes
    // or those whose namespace doesn't map to a known provider.
    private static MigrationRawEntry? TryExtractMigration(GeneratorAttributeSyntaxContext ctx)
    {
        var type = (INamedTypeSymbol)ctx.TargetSymbol;
        if (type.IsAbstract) return null;

        var provider = ProviderFromNamespace(type);
        if (provider is null) return null;

        var attr = ctx.Attributes[0];
        if (attr.ConstructorArguments is not [{ Value: string migId }]) return null;

        return new MigrationRawEntry(provider, migId, type.ToDisplayString());
    }

    // Called for every class bearing [DbContext(...)]. Returns null unless the class also
    // inherits from ModelSnapshot (design-time context factories share the attribute).
    private static ProviderSnapshot? TryExtractSnapshot(GeneratorAttributeSyntaxContext ctx)
    {
        var type = (INamedTypeSymbol)ctx.TargetSymbol;
        if (type.IsAbstract) return null;

        var snapshotBase = ctx.SemanticModel.Compilation.GetTypeByMetadataName(
            "Microsoft.EntityFrameworkCore.Infrastructure.ModelSnapshot");
        if (snapshotBase is null || !InheritsFrom(type, snapshotBase)) return null;

        var provider = ProviderFromNamespace(type);
        if (provider is null) return null;

        return new ProviderSnapshot(provider, type.ToDisplayString());
    }

    private static MigrationData AssembleMigrationData(
        ImmutableArray<MigrationRawEntry> rawMigrations,
        ImmutableArray<ProviderSnapshot> rawSnapshots)
    {
        var groups = new Dictionary<string, List<MigrationEntry>>();
        foreach (var m in rawMigrations)
        {
            if (!groups.TryGetValue(m.Provider, out var list))
                groups[m.Provider] = list = new List<MigrationEntry>();
            list.Add(new MigrationEntry(m.Id, m.FqTypeName));
        }

        var migrations = ImmutableArray.CreateBuilder<ProviderMigrations>();
        foreach (var kv in groups)
        {
            kv.Value.Sort(static (a, b) => string.CompareOrdinal(a.Id, b.Id));
            migrations.Add(new ProviderMigrations(kv.Key,
                new EquatableArray<MigrationEntry>(kv.Value.ToImmutableArray())));
        }

        return new MigrationData(
            new EquatableArray<ProviderMigrations>(migrations.ToImmutable()),
            new EquatableArray<ProviderSnapshot>(rawSnapshots));
    }

    // Roslyn helpers

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(
                    current.OriginalDefinition, baseType.OriginalDefinition))
                return true;
            current = current.BaseType;
        }
        return false;
    }

    // Matched against namespace segments by prefix, so migrations that came from a per-provider
    // assembly (e.g. Bit.SqliteMigrations.Migrations) resolve the same as a plain Sqlite segment.
    // Longest spelling first where one prefix would also match another.
    private static readonly (string Prefix, string Provider)[] ProviderNamespacePrefixes =
    [
        ("SqlServer", "SqlServer"),
        ("Sqlite", "Sqlite"),
        ("PostgreSql", "PostgreSql"),
        ("Postgres", "PostgreSql"),
        ("Npgsql", "PostgreSql"),
        ("MySql", "MySql"),
    ];

    /// <summary>
    /// Determines the EF provider for a migration or snapshot type by inspecting
    /// the namespace segments for a known provider keyword.
    /// </summary>
    private static string? ProviderFromNamespace(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        foreach (var segment in ns.Split('.'))
        {
            foreach (var (prefix, provider) in ProviderNamespacePrefixes)
            {
                if (segment.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return provider;
            }
        }
        return null;
    }

    // Migration data helpers

    private static ImmutableArray<MigrationEntry> GetMigrations(MigrationData data, string provider)
    {
        foreach (var pm in data.Migrations.Array)
            if (pm.Provider == provider) return pm.Entries.Array;
        return ImmutableArray<MigrationEntry>.Empty;
    }

    private static string? GetSnapshot(MigrationData data, string provider)
    {
        foreach (var ps in data.Snapshots.Array)
            if (ps.Provider == provider) return ps.FqTypeName;
        return null;
    }

    // Code generation entry point

    private static void GenerateForAttribute(
        SourceProductionContext spc,
        AttributeInfo attr,
        MigrationData migrationData,
        EquatableArray<ProviderSpec> providers,
        BuildProperties buildProperties)
    {
        spc.AddSource(
            $"{attr.SchemaName}.g.cs",
            SourceText.From(
                Emit(attr, migrationData, providers.Array, buildProperties),
                System.Text.Encoding.UTF8));
    }

    // Provider call shapes shared with the emitter

    /// <summary>Returns the DbContextOptionsBuilder extension method call for a given provider.</summary>
    private static string ProviderUseCall(string providerName) => providerName switch
    {
        "Sqlite" => "UseSqlite(opts.ConnectionString)",
        "SqlServer" => "UseSqlServer(opts.ConnectionString)",
        "PostgreSql" => "UseNpgsql(opts.ConnectionString)",
        "MySql" => "UseMySql(opts.ConnectionString, ServerVersion.AutoDetect(opts.ConnectionString))",
        _ => throw new System.InvalidOperationException($"Unknown provider: {providerName}"),
    };


    /// <summary>Returns the user-facing description for a provider's connection string option.</summary>
    private static string ProviderDescription(string name) => name switch
    {
        "Sqlite" => "SQLite connection string",
        "SqlServer" => "SQL Server connection string",
        "PostgreSql" => "PostgreSQL connection string",
        "MySql" => "MySQL connection string",
        _ => $"{name} connection string",
    };

    // Utilities

    private static string LowerFirst(string s) =>
        s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

    // Pipeline data types
    //
    // The types below carry plain-string data extracted from Roslyn symbols and implement value
    // equality. Roslyn compares each pipeline step's output with EqualityComparer<T>.Default,
    // and when the output matches the previous run the downstream steps are skipped.
    //
    // ImmutableArray<T> doesn't implement structural equality on its own, so EquatableArray<T>
    // wraps it and compares element-wise for any T that is IEquatable<T>.

    // Compiler-visible build properties: the project directory, the resource-name prefix scripts
    // are embedded under, and the prefix an inherited journal may still record them under.
    private readonly record struct BuildProperties(
        string? ProjectDir,
        string? ScriptPrefix,
        string? PreviousScriptPrefix);

    // Provider descriptor; a record struct so it compares by value.
    private readonly record struct ProviderSpec(string Name, string Assembly);

    // Data pulled off the DatabaseSetup<T> attribute.
    private readonly record struct AttributeInfo(
        string Ns, string TypeName, string SchemaName, string MigratorKey);

    // Transient pipeline entry, before grouping into ProviderMigrations.
    private readonly record struct MigrationRawEntry(string Provider, string Id, string FqTypeName);

    // A single EF Core migration class.
    private readonly record struct MigrationEntry(string Id, string FqTypeName);

    // Per-provider collection of migration entries.
    private readonly record struct ProviderMigrations(
        string Provider, EquatableArray<MigrationEntry> Entries);

    // Per-provider model snapshot type name.
    private readonly record struct ProviderSnapshot(string Provider, string FqTypeName);

    // All migration and snapshot data discovered in the compilation.
    private readonly record struct MigrationData(
        EquatableArray<ProviderMigrations> Migrations,
        EquatableArray<ProviderSnapshot> Snapshots);

    /// <summary>
    /// Wraps <see cref="ImmutableArray{T}"/> with structural (element-wise) equality so
    /// Roslyn's incremental caching layer can tell when a pipeline step's output hasn't
    /// changed, even though the compilation object is a new instance.
    /// </summary>
    private readonly struct EquatableArray<T>(ImmutableArray<T> array) : System.IEquatable<EquatableArray<T>>
        where T : System.IEquatable<T>
    {
        public static readonly EquatableArray<T> Empty = new EquatableArray<T>(ImmutableArray<T>.Empty);

        public ImmutableArray<T> Array { get; } = array;

        public bool Equals(EquatableArray<T> other)
        {
            if (Array.IsDefault && other.Array.IsDefault) return true;
            if (Array.IsDefault || other.Array.IsDefault) return false;
            if (Array.Length != other.Array.Length) return false;
            for (var i = 0; i < Array.Length; i++)
                if (!Array[i].Equals(other.Array[i])) return false;
            return true;
        }

        public override bool Equals(object? obj)
            => obj is EquatableArray<T> other && Equals(other);

        public override int GetHashCode()
        {
            if (Array.IsDefault) return 0;
            unchecked
            {
                var hash = 17;
                foreach (var item in Array)
                    hash = hash * 31 + item.GetHashCode();
                return hash;
            }
        }
    }
}
