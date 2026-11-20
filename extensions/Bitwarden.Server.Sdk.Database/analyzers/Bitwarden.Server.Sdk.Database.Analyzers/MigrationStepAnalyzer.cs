using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Bitwarden.Server.Sdk.Database.Analyzers;

/// <summary>
/// Reports steps in an EF Core migration's <c>Up</c> that the release before it can't run against.
/// During a rolling deployment the previous release keeps running on the migrated schema, so a
/// column it reads must outlive it: a drop or a rename waits for the release after the one that
/// stopped using it. Steps on a table the same migration creates are never reported, since no
/// earlier release knows that table.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MigrationStepAnalyzer : DiagnosticAnalyzer
{
    private const string HelpUrlFormat = "https://github.com/bitwarden/dotnet-extensions/blob/main/docs/diagnostics.md#{0}";

    /// <summary>
    /// Reported for a step that removes or reshapes something the previous release may still use.
    /// </summary>
    internal static readonly DiagnosticDescriptor BreaksPreviousRelease = new(
        id: "BW0026",
        title: "Migration step breaks the previous release",
        messageFormat: "{0} breaks the previous release, which still runs against this schema while this "
            + "release rolls out: {1}. Make the change over two releases, or suppress this with a "
            + "justification naming the release that stopped using it.",
        category: "Bitwarden.Database",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: string.Format(HelpUrlFormat, "BW0026"));

    /// <summary>
    /// Reported for a step that adds a rule existing rows, or the previous release's writes, may
    /// not satisfy.
    /// </summary>
    internal static readonly DiagnosticDescriptor MayRefuseWrites = new(
        id: "BW0027",
        title: "Migration step can refuse existing rows or the previous release's writes",
        messageFormat: "{0} can refuse rows already in '{1}', or rows the previous release writes while "
            + "this release rolls out. Confirm both satisfy it, and suppress this with a justification "
            + "saying why.",
        category: "Bitwarden.Database",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: string.Format(HelpUrlFormat, "BW0027"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(BreaksPreviousRelease, MayRefuseWrites);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        // Some projects mark migrations as generated to quiet style rules; they still need checking.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterCompilationStartAction(start =>
        {
            var migrationBuilder = start.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder");
            var migration = start.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Migrations.Migration");
            if (migrationBuilder is null || migration is null)
            {
                return;
            }

            start.RegisterOperationBlockAction(block => AnalyzeBlock(block, migrationBuilder, migration));
        });
    }

    private static void AnalyzeBlock(OperationBlockAnalysisContext context, INamedTypeSymbol migrationBuilder, INamedTypeSymbol migration)
    {
        if (context.OwningSymbol is not IMethodSymbol { Name: "Up", IsOverride: true } method
            || method.Parameters.Length != 1
            || !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, migrationBuilder)
            || !DerivesFrom(method.ContainingType, migration))
        {
            return;
        }

        var steps = context.OperationBlocks
            .SelectMany(block => block.DescendantsAndSelf())
            .OfType<IInvocationOperation>()
            .Where(invocation => SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, migrationBuilder))
            .ToList();

        var created = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in steps.Where(step => step.TargetMethod.Name == "CreateTable"))
        {
            if (Text(step, "name") is { Known: true, Value: { } name })
            {
                created.Add(TableKey(Text(step, "schema").Value, name));
            }
        }

        foreach (var step in steps)
        {
            Check(context, step, created);
        }
    }

    private static void Check(OperationBlockAnalysisContext context, IInvocationOperation step, HashSet<string> created)
    {
        var operation = step.TargetMethod.Name;
        switch (operation)
        {
            case "DropTable":
                ReportUnlessCreated(context, step, created, "name", BreaksPreviousRelease, "it drops a table the previous release may still use");
                break;
            case "RenameTable":
                ReportUnlessCreated(context, step, created, "name", BreaksPreviousRelease, "the previous release still uses the old name");
                break;
            case "DropColumn":
                ReportUnlessCreated(context, step, created, "table", BreaksPreviousRelease, "it drops a column the previous release may still read or write");
                break;
            case "RenameColumn":
                ReportUnlessCreated(context, step, created, "table", BreaksPreviousRelease, "the previous release still uses the old name");
                break;
            case "DropPrimaryKey":
                ReportUnlessCreated(context, step, created, "table", BreaksPreviousRelease, "it changes the key the previous release identifies rows by");
                break;
            case "DropSchema":
                Report(context, step, BreaksPreviousRelease, "it drops a schema the previous release may still use");
                break;
            case "DropSequence":
                Report(context, step, BreaksPreviousRelease, "it drops a sequence the previous release may still use");
                break;
            case "RenameSequence":
                Report(context, step, BreaksPreviousRelease, "the previous release still uses the old name");
                break;
            case "AddColumn":
                if (AddsRequiredColumnWithoutDefault(step))
                {
                    ReportUnlessCreated(context, step, created, "table", BreaksPreviousRelease, "it adds a NOT NULL column with no default, which the previous release's inserts don't write");
                }

                break;
            case "AlterColumn":
                if (NarrowingReason(step) is { } reason)
                {
                    ReportUnlessCreated(context, step, created, "table", BreaksPreviousRelease, reason);
                }

                break;
            case "CreateIndex":
                if (Flag(step, "unique") is not false)
                {
                    ReportUnlessCreated(context, step, created, "table", MayRefuseWrites, null);
                }

                break;
            case "AddUniqueConstraint":
            case "AddCheckConstraint":
                ReportUnlessCreated(context, step, created, "table", MayRefuseWrites, null);
                break;
        }
    }

    private static bool AddsRequiredColumnWithoutDefault(IInvocationOperation step)
        => Flag(step, "nullable") is false
            && Flag(step, "rowVersion") is not true
            && IsAbsent(step, "defaultValue")
            && IsAbsent(step, "defaultValueSql")
            && IsAbsent(step, "computedColumnSql");

    /// <summary>
    /// Says how an <c>AlterColumn</c> narrows the column, or returns null when it only widens it or
    /// changes nothing the previous release depends on. A value that isn't a constant counts as a
    /// change, since it can't be compared.
    /// </summary>
    private static string? NarrowingReason(IInvocationOperation step)
    {
        var type = step.TargetMethod.TypeArguments.FirstOrDefault();
        if (type is not null && Argument(step, "oldClrType")?.Value is ITypeOfOperation oldClrType
            && !SymbolEqualityComparer.Default.Equals(Underlying(type), Underlying(oldClrType.TypeOperand)))
        {
            return "it changes the column's type";
        }

        if (Flag(step, "nullable") is false && Flag(step, "oldNullable") is not false)
        {
            return "it makes the column NOT NULL, which the previous release may not write";
        }

        if (Differs(Text(step, "collation"), OldCollation(step)))
        {
            return "it changes the column's collation, which changes how values compare and rewrites the table on SQL Server";
        }

        var storeType = Text(step, "type");
        var oldStoreType = Text(step, "oldType");
        if (Differs(storeType, oldStoreType) && !IsLongerOfSameType(storeType.Value, oldStoreType.Value))
        {
            return "it changes the column's store type";
        }

        if (Number(step, "maxLength") is { } maxLength && (Number(step, "oldMaxLength") is not { } oldMaxLength || maxLength < oldMaxLength))
        {
            return "it shortens the column";
        }

        if (Number(step, "precision") < Number(step, "oldPrecision") || Number(step, "scale") < Number(step, "oldScale"))
        {
            return "it reduces the column's precision or scale";
        }

        if (Differs(Text(step, "computedColumnSql"), Text(step, "oldComputedColumnSql")))
        {
            return "it changes how the column is computed";
        }

        return null;
    }

    /// <summary>
    /// The column's collation before the step. Some providers (Pomelo's MySQL provider among them)
    /// scaffold it as a chained <c>.OldAnnotation("Relational:Collation", ...)</c> rather than the
    /// <c>oldCollation</c> argument.
    /// </summary>
    private static (bool Known, string? Value) OldCollation(IInvocationOperation step)
    {
        if (Argument(step, "oldCollation") is { ArgumentKind: not ArgumentKind.DefaultValue })
        {
            return Text(step, "oldCollation");
        }

        for (var parent = step.Parent; parent is IInvocationOperation chained; parent = chained.Parent)
        {
            if (chained.TargetMethod.Name == "OldAnnotation"
                && chained.Arguments.Length == 2
                && chained.Arguments[0].Value.ConstantValue is { HasValue: true, Value: "Relational:Collation" })
            {
                var value = chained.Arguments[1].Value;
                while (value is IConversionOperation conversion && !value.ConstantValue.HasValue)
                {
                    value = conversion.Operand;
                }

                return value.ConstantValue is { HasValue: true, Value: null or string }
                    ? (true, (string?)value.ConstantValue.Value)
                    : (false, null);
            }
        }

        return (true, null);
    }

    /// <summary>
    /// True when both store types name the same type and the new length is at least the old one,
    /// <c>max</c> being longest: <c>nvarchar(100)</c> to <c>nvarchar(200)</c> or <c>nvarchar(max)</c>.
    /// </summary>
    private static bool IsLongerOfSameType(string? type, string? oldType)
    {
        if (type is null || oldType is null || !TrySplit(type, out var name, out var length) || !TrySplit(oldType, out var oldName, out var oldLength))
        {
            return false;
        }

        return string.Equals(name, oldName, StringComparison.OrdinalIgnoreCase) && length >= oldLength;
    }

    private static bool TrySplit(string storeType, out string name, out long length)
    {
        name = storeType;
        length = 0;
        var open = storeType.IndexOf('(');
        if (open < 0 || !storeType.EndsWith(")", StringComparison.Ordinal))
        {
            return false;
        }

        name = storeType.Substring(0, open).Trim();
        var inner = storeType.Substring(open + 1, storeType.Length - open - 2).Trim();
        if (string.Equals(inner, "max", StringComparison.OrdinalIgnoreCase))
        {
            length = long.MaxValue;
            return true;
        }

        return long.TryParse(inner, out length);
    }

    private static void ReportUnlessCreated(
        OperationBlockAnalysisContext context,
        IInvocationOperation step,
        HashSet<string> created,
        string tableParameter,
        DiagnosticDescriptor descriptor,
        string? reason)
    {
        var table = Text(step, tableParameter);
        if (table is { Known: true, Value: { } name } && created.Contains(TableKey(Text(step, "schema").Value, name)))
        {
            return;
        }

        Report(context, step, descriptor, reason ?? table.Value ?? tableParameter);
    }

    private static void Report(OperationBlockAnalysisContext context, IInvocationOperation step, DiagnosticDescriptor descriptor, string detail)
    {
        // The step's name alone, without a generic call's type argument.
        var location = step.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member }
            ? member.Name.Identifier.GetLocation()
            : step.Syntax.GetLocation();
        context.ReportDiagnostic(Diagnostic.Create(descriptor, location, step.TargetMethod.Name, detail));
    }

    private static bool DerivesFrom(INamedTypeSymbol? type, INamedTypeSymbol baseType)
    {
        for (var current = type?.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static ITypeSymbol Underlying(ITypeSymbol type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;

    private static string TableKey(string? schema, string table) => (schema ?? string.Empty) + "\u0000" + table;

    private static IArgumentOperation? Argument(IInvocationOperation step, string parameter)
        => step.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == parameter);

    private static Optional<object?> Constant(IInvocationOperation step, string parameter)
    {
        var value = Argument(step, parameter)?.Value;
        while (value is IConversionOperation conversion && !value.ConstantValue.HasValue)
        {
            value = conversion.Operand;
        }

        return value?.ConstantValue ?? new Optional<object?>(null);
    }

    /// <summary>
    /// True when the argument is left out or passed as null, so the column gets no default.
    /// </summary>
    private static bool IsAbsent(IInvocationOperation step, string parameter)
    {
        var argument = Argument(step, parameter);
        if (argument is null || argument.ArgumentKind == ArgumentKind.DefaultValue)
        {
            return true;
        }

        var constant = Constant(step, parameter);
        return constant.HasValue && constant.Value is null;
    }

    private static bool? Flag(IInvocationOperation step, string parameter)
        => Constant(step, parameter) is { HasValue: true, Value: bool value } ? value : null;

    private static int? Number(IInvocationOperation step, string parameter)
        => Constant(step, parameter) is { HasValue: true, Value: int value } ? value : null;

    private static (bool Known, string? Value) Text(IInvocationOperation step, string parameter)
    {
        var constant = Constant(step, parameter);
        return constant.HasValue && constant.Value is null or string ? (true, (string?)constant.Value) : (false, null);
    }

    private static bool Differs((bool Known, string? Value) value, (bool Known, string? Value) old)
        => !value.Known || !old.Known || !string.Equals(value.Value, old.Value, StringComparison.Ordinal);
}
