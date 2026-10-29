using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
    //
    // A Name may be served by more than one package when they share an API. Microting's fork of
    // Pomelo keeps Pomelo's UseMySql and ServerVersion, so the generated calls are identical and
    // only the assembly name differs; whichever one the project references is the one used.
    private static readonly ProviderSpec[] AllProviders =
    [
        new("Sqlite", "Microsoft.EntityFrameworkCore.Sqlite"),
        new("SqlServer", "Microsoft.EntityFrameworkCore.SqlServer"),
        new("PostgreSql", "Npgsql.EntityFrameworkCore.PostgreSQL"),
        new("MySql", "Pomelo.EntityFrameworkCore.MySql"),
        new("MySql", "Microting.EntityFrameworkCore.MySql"),
    ];

    /// <summary>
    /// Reported when the previous and current script prefixes overlap. The journal rewrite exists
    /// to adopt a journal written under an unrelated name, and is only idempotent for prefixes
    /// where neither contains the other. Both come from build properties, so this is decidable
    /// here rather than left to fail against a production database.
    /// </summary>
    private static readonly DiagnosticDescriptor OverlappingScriptPrefixes = new(
        id: "BWDB001",
        title: "Previous script prefix overlaps the current one",
        messageFormat: "BitPreviousSqlServerScriptPrefix '{0}' and BitSqlServerScriptPrefix '{1}' "
            + "overlap: one is a prefix of the other. The journal rewrite only supports adopting an "
            + "unrelated name. Set BitPreviousSqlServerScriptPrefix to the name the journal actually "
            + "records, or remove it if the journal already matches this build.",
        category: "Bitwarden.Database",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Reported when two packages serving the same provider are both referenced. They define the
    /// same types in the same namespaces, so the generated code couldn't bind to either, and the
    /// runtime dispatch could only ever match one of them.
    /// </summary>
    private static readonly DiagnosticDescriptor ConflictingProviderPackages = new(
        id: "BWDB002",
        title: "More than one package is referenced for the same database provider",
        messageFormat: "The {0} provider is referenced through more than one package: {1}. "
            + "Reference only one of them.",
        category: "Bitwarden.Database",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Reported when an engine is declared on the attribute but none of its provider packages is
    /// referenced, which would otherwise surface as an unresolved call inside generated code.
    /// </summary>
    private static readonly DiagnosticDescriptor MissingProviderPackage = new(
        id: "BWDB003",
        title: "A declared database provider's package is not referenced",
        messageFormat: "The {0} provider is declared on DatabaseSetup but its package isn't referenced. "
            + "Reference {1}.",
        category: "Bitwarden.Database",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Reported when the context is sealed. Each EF Core engine gets a subcontext derived from it,
    /// which would otherwise fail to compile inside generated code.
    /// </summary>
    private static readonly DiagnosticDescriptor SealedContext = new(
        id: "BWDB004",
        title: "The DatabaseSetup context is sealed",
        messageFormat: "{0} is sealed, but DatabaseSetup derives a context from it for each EF Core engine. "
            + "Remove sealed from {0}.",
        category: "Bitwarden.Database",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Reported when no constructor the subcontexts can call takes the context's options.
    /// </summary>
    private static readonly DiagnosticDescriptor NoOptionsConstructor = new(
        id: "BWDB005",
        title: "The DatabaseSetup context has no constructor taking its options",
        messageFormat: "{0} needs a non-private constructor with one DbContextOptions or DbContextOptions<{0}> "
            + "parameter, and no ref, out, in or params parameters, for DatabaseSetup to derive a context from it",
        category: "Bitwarden.Database",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Reported when <c>DesignTimeServices</c> names a type the generated factories can't use.
    /// </summary>
    private static readonly DiagnosticDescriptor InvalidDesignTimeServices = new(
        id: "BWDB006",
        title: "DesignTimeServices can't be used",
        messageFormat: "DesignTimeServices type {0} must be a non-abstract class implementing "
            + "IDatabaseDesignTimeServices with a public parameterless constructor",
        category: "Bitwarden.Database",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Reported when SQL Server is set to EF Core migrations but isn't one of the declared engines.
    /// </summary>
    private static readonly DiagnosticDescriptor SqlServerMigrationsWithoutSqlServer = new(
        id: "BWDB007",
        title: "SqlServerMigrations is set but SQL Server isn't declared",
        messageFormat: "SqlServerMigrations = EfCore has no effect: DatabaseProvider.SqlServer isn't among the "
            + "declared providers",
        category: "Bitwarden.Database",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Reported when the context's constructor needs more than its options and nothing registers it.
    /// The design-time factories would otherwise fail when EF Core tooling runs them.
    /// </summary>
    private static readonly DiagnosticDescriptor MissingDesignTimeServices = new(
        id: "BWDB008",
        title: "The DatabaseSetup context needs DesignTimeServices",
        messageFormat: "{0}'s constructor takes more than its options, so EF Core tooling can't construct it. "
            + "Set DesignTimeServices to a type implementing IDatabaseDesignTimeServices that registers them.",
        category: "Bitwarden.Database",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static bool PrefixesOverlap(string previous, string current)
        => previous.Length != current.Length
            && (previous.StartsWith(current, StringComparison.Ordinal)
                || current.StartsWith(previous, StringComparison.Ordinal));

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
    /// in the compilation's referenced assembly list, transitive ones included. Generation keeps
    /// only the engines the attribute declares. Two packages for the same provider both come
    /// back, so generation can report the conflict.
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

        var sqlServerEfCore = attr.NamedArguments
            .FirstOrDefault(kv => kv.Key == "SqlServerMigrations").Value is { Type: INamedTypeSymbol migrationsEnum, Value: { } value }
            && EnumMemberName(migrationsEnum, value) == "EfCore";

        var designTimeServices = attr.NamedArguments
            .FirstOrDefault(kv => kv.Key == "DesignTimeServices").Value.Value as INamedTypeSymbol;

        return new AttributeInfo(
            ns,
            typeName,
            schemaName,
            migratorKey,
            DeclaredProviders(attr),
            sqlServerEfCore,
            contextType.IsSealed,
            FindConstructor(contextType),
            designTimeServices?.ToDisplayString(FullyQualified),
            designTimeServices is not null && !IsUsableDesignTimeServices(designTimeServices));
    }

    private static string? EnumMemberName(INamedTypeSymbol enumType, object value)
        => enumType.GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(f => f.HasConstantValue && Equals(f.ConstantValue, value))
            ?.Name;

    private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// The constructor the subcontexts call: the non-private one with the most parameters, as
    /// dependency injection would choose, among those taking exactly one <c>DbContextOptions</c> or
    /// <c>DbContextOptions&lt;TContext&gt;</c> and no ref, out, in or params parameters.
    /// </summary>
    private static ContextConstructor? FindConstructor(INamedTypeSymbol context)
    {
        ContextConstructor? best = null;
        var bestCount = -1;

        foreach (var ctor in context.InstanceConstructors)
        {
            if (ctor.DeclaredAccessibility == Accessibility.Private || ctor.Parameters.Length <= bestCount)
                continue;

            var optionsIndex = -1;
            var genericOptions = false;
            var usable = true;
            var parameters = ImmutableArray.CreateBuilder<ConstructorParameter>();

            for (var i = 0; i < ctor.Parameters.Length && usable; i++)
            {
                var parameter = ctor.Parameters[i];
                if (parameter.RefKind != RefKind.None || parameter.IsParams)
                {
                    usable = false;
                    break;
                }

                if (IsOptions(parameter.Type, context, out var generic))
                {
                    usable = optionsIndex < 0;
                    optionsIndex = i;
                    genericOptions = generic;
                }

                parameters.Add(new ConstructorParameter(
                    parameter.Type.ToDisplayString(FullyQualified), parameter.Name, DefaultOf(parameter)));
            }

            if (!usable || optionsIndex < 0)
                continue;

            best = new ContextConstructor(
                new EquatableArray<ConstructorParameter>(parameters.ToImmutable()), optionsIndex, genericOptions);
            bestCount = ctor.Parameters.Length;
        }

        return best;
    }

    private static bool IsOptions(ITypeSymbol type, INamedTypeSymbol context, out bool generic)
    {
        generic = false;
        if (type is not INamedTypeSymbol { Name: "DbContextOptions" } named
            || named.ContainingNamespace?.ToDisplayString() != "Microsoft.EntityFrameworkCore")
        {
            return false;
        }

        if (named.Arity == 0)
            return true;

        generic = named.Arity == 1 && SymbolEqualityComparer.Default.Equals(named.TypeArguments[0], context);
        return generic;
    }

    /// <summary>A parameter's default value as C# source, or null when it has none.</summary>
    private static string? DefaultOf(IParameterSymbol parameter)
    {
        if (!parameter.HasExplicitDefaultValue)
            return null;

        var value = parameter.ExplicitDefaultValue;
        if (value is null)
            return "default";

        var literal = SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false);
        return parameter.Type.TypeKind == TypeKind.Enum
            ? $"({parameter.Type.ToDisplayString(FullyQualified)}){literal}"
            : value switch
            {
                float => literal + "F",
                decimal => literal + "M",
                _ => literal,
            };
    }

    private static bool IsUsableDesignTimeServices(INamedTypeSymbol type)
        => type is { TypeKind: TypeKind.Class, IsAbstract: false, IsUnboundGenericType: false }
            && type.AllInterfaces.Any(i =>
                i.ToDisplayString() == "Bitwarden.Server.Sdk.Database.IDatabaseDesignTimeServices")
            && type.InstanceConstructors.Any(c =>
                c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0);

    /// <summary>
    /// The engines named in the attribute's constructor argument, by <c>DatabaseProvider</c>
    /// member name, which is also the <see cref="ProviderSpec.Name"/> they match.
    /// </summary>
    private static EquatableArray<string> DeclaredProviders(AttributeData attr)
    {
        if (attr.ConstructorArguments is not [{ Kind: TypedConstantKind.Array } providers]
            || attr.AttributeConstructor?.Parameters[0].Type is not IArrayTypeSymbol { ElementType: INamedTypeSymbol providerEnum })
        {
            return EquatableArray<string>.Empty;
        }

        var names = providerEnum.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => f.HasConstantValue)
            .ToDictionary(f => f.ConstantValue!, f => f.Name);

        var declared = ImmutableArray.CreateBuilder<string>();
        foreach (var value in providers.Values)
        {
            if (value.Value is { } v && names.TryGetValue(v, out var name) && !declared.Contains(name))
                declared.Add(name);
        }

        return new EquatableArray<string>(declared.ToImmutable());
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
        if (buildProperties.PreviousScriptPrefix is { } previous
            && buildProperties.ScriptPrefix is { } current
            && PrefixesOverlap(previous, current))
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                OverlappingScriptPrefixes, Location.None, previous, current));
            return;
        }

        // Exactly the declared engines, in AllProviders order so the output is stable. Packages
        // referenced for anything else, such as tests or a transitive dependency, play no part.
        var declared = attr.Providers.Array;
        var selected = providers.Array.Where(p => declared.Contains(p.Name)).ToImmutableArray();
        var failed = false;

        foreach (var name in declared)
        {
            if (selected.Any(p => p.Name == name))
                continue;

            spc.ReportDiagnostic(Diagnostic.Create(
                MissingProviderPackage,
                Location.None,
                name,
                string.Join(" or ", AllProviders.Where(p => p.Name == name).Select(p => p.Assembly))));
            failed = true;
        }

        foreach (var conflict in selected.GroupBy(p => p.Name).Where(g => g.Count() > 1))
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                ConflictingProviderPackages,
                Location.None,
                conflict.Key,
                string.Join(", ", conflict.Select(p => p.Assembly))));
            failed = true;
        }

        if (attr.SqlServerEfCore && !declared.Contains("SqlServer"))
        {
            spc.ReportDiagnostic(Diagnostic.Create(SqlServerMigrationsWithoutSqlServer, Location.None));
            attr = attr with { SqlServerEfCore = false };
        }

        if (attr.InvalidDesignTimeServices)
        {
            spc.ReportDiagnostic(Diagnostic.Create(InvalidDesignTimeServices, Location.None, attr.DesignTimeServices));
            failed = true;
        }

        // Only an engine migrated by EF Core gets a subcontext, so only then does the context's
        // shape matter.
        if (selected.Any(p => IsEfCore(attr, p)))
        {
            if (attr.ContextSealed)
            {
                spc.ReportDiagnostic(Diagnostic.Create(SealedContext, Location.None, attr.TypeName));
                failed = true;
            }
            else if (attr.Constructor is not { } ctor)
            {
                spc.ReportDiagnostic(Diagnostic.Create(NoOptionsConstructor, Location.None, attr.TypeName));
                failed = true;
            }
            else if (attr.DesignTimeServices is null
                && ctor.Parameters.Array.Where((_, i) => i != ctor.OptionsIndex).Any(p => p.Default is null))
            {
                spc.ReportDiagnostic(Diagnostic.Create(MissingDesignTimeServices, Location.None, attr.TypeName));
                failed = true;
            }
        }

        if (failed)
            return;

        spc.AddSource(
            $"{attr.SchemaName}.g.cs",
            SourceText.From(
                Emit(attr, migrationData, selected, buildProperties),
                System.Text.Encoding.UTF8));
    }

    /// <summary>Whether EF Core migrates the provider: every engine but SQL Server, unless the schema opts it in.</summary>
    private static bool IsEfCore(AttributeInfo attr, ProviderSpec provider)
        => provider.Name != "SqlServer" || attr.SqlServerEfCore;

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
        string Ns,
        string TypeName,
        string SchemaName,
        string MigratorKey,
        EquatableArray<string> Providers,
        bool SqlServerEfCore,
        bool ContextSealed,
        ContextConstructor? Constructor,
        string? DesignTimeServices,
        bool InvalidDesignTimeServices);

    // The context constructor the subcontexts forward to, and which parameter takes the options.
    private readonly record struct ContextConstructor(
        EquatableArray<ConstructorParameter> Parameters, int OptionsIndex, bool GenericOptions);

    // A constructor parameter as source: its fully qualified type, name and default, if any.
    private readonly record struct ConstructorParameter(string Type, string Name, string? Default);

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
