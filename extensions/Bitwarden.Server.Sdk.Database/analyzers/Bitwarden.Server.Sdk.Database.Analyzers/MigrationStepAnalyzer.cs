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
/// stopped using it. Steps on a table created earlier in the same <c>Up</c> aren't reported, since
/// no earlier release knows that table, apart from a foreign key from it to an existing table.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MigrationStepAnalyzer : DiagnosticAnalyzer
{
    private const string HelpUrlFormat = "https://github.com/bitwarden/dotnet-extensions/blob/main/docs/diagnostics.md#{0}";

    // EF Core's ReferentialAction values; the analyzer doesn't reference EF Core.
    private const int ReferentialActionNoAction = 0;
    private const int ReferentialActionRestrict = 1;

    /// <summary>
    /// Reported for a step that removes or reshapes something the previous release may still use.
    /// </summary>
    internal static readonly DiagnosticDescriptor BreaksPreviousRelease = new(
        id: "BW0026",
        title: "Migration step breaks the previous release",
        messageFormat: "{0} breaks the previous release: {1}. The previous release still runs against this "
            + "schema until this release finishes rolling out. Make the change across releases instead, or "
            + "suppress this warning with a justification saying why the previous release is unaffected.",
        category: "Bitwarden.Database",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: string.Format(HelpUrlFormat, "BW0026"));

    /// <summary>
    /// Reported for a constraint added to an existing table, which rows already there, or the
    /// previous release's writes, may not satisfy.
    /// </summary>
    internal static readonly DiagnosticDescriptor MayRefuseWrites = new(
        id: "BW0027",
        title: "Migration step adds a constraint to an existing table",
        messageFormat: "{0} adds {1} to {2}. The migration fails if rows already there don't satisfy it, "
            + "and the previous release's writes fail if they don't satisfy it while this release rolls "
            + "out. Confirm that both satisfy it, then suppress this warning with a justification saying why.",
        category: "Bitwarden.Database",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: string.Format(HelpUrlFormat, "BW0027"));

    /// <summary>
    /// Store types with no length limit, each with the bounded types it widens. Moving to one of
    /// these from a bounded type removes the limit rather than narrowing the column.
    /// </summary>
    private static readonly ImmutableDictionary<string, ImmutableArray<string>> UnboundedTypes =
        ImmutableDictionary.CreateRange(
            StringComparer.OrdinalIgnoreCase,
            [
                // PostgreSQL
                new KeyValuePair<string, ImmutableArray<string>>("text", ["character varying", "varchar", "character", "char"]),
                // MySQL and MariaDB
                new KeyValuePair<string, ImmutableArray<string>>("longtext", ["varchar", "char", "tinytext", "text", "mediumtext"]),
                new KeyValuePair<string, ImmutableArray<string>>("longblob", ["varbinary", "binary", "tinyblob", "blob", "mediumblob"]),
            ]);

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

        // Steps are checked in source order, so a table counts as new only after its CreateTable:
        // a drop or rename of a table before one of the same name is created still breaks the
        // previous release.
        var steps = context.OperationBlocks
            .SelectMany(block => block.DescendantsAndSelf())
            .OfType<IInvocationOperation>()
            .Where(invocation => SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, migrationBuilder))
            .OrderBy(invocation => invocation.Syntax.SpanStart);

        var created = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in steps)
        {
            Check(context, step, created);
        }
    }

    private static void Check(OperationBlockAnalysisContext context, IInvocationOperation step, HashSet<string> created)
    {
        switch (step.TargetMethod.Name)
        {
            case "CreateTable":
                if (Text(step, "name") is { Known: true, Value: { } name })
                {
                    created.Add(TableKey(Text(step, "schema").Value, name));
                }

                foreach (var foreignKey in step.Descendants().OfType<IInvocationOperation>().Where(call => call.TargetMethod is { Name: "ForeignKey", ContainingType.Name: "CreateTableBuilder" }))
                {
                    ReportIfRefusesDeletes(context, foreignKey, created);
                }

                break;
            case "DropTable":
                ReportUnlessCreated(context, step, created, "name", BreaksPreviousRelease, "it drops a table the previous release may still use");
                Forget(step, created);
                break;
            case "RenameTable":
                var renamedNewTable = IsCreated(step, created, "name");
                ReportUnlessCreated(context, step, created, "name", BreaksPreviousRelease, "the previous release still uses the old name");
                Forget(step, created);
                if (renamedNewTable && Text(step, "newName") is { Known: true, Value: { } newName })
                {
                    var newSchema = Argument(step, "newSchema") is { ArgumentKind: not ArgumentKind.DefaultValue }
                        ? Text(step, "newSchema").Value
                        : Text(step, "schema").Value;
                    created.Add(TableKey(newSchema, newName));
                }

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
                if (Flag(step, "unique") is { } unique)
                {
                    if (unique)
                    {
                        ReportConstraintUnlessCreated(context, step, created, "a unique index");
                    }
                }
                else
                {
                    ReportConstraintUnlessCreated(context, step, created, "an index that may be unique");
                }

                break;
            case "AddUniqueConstraint":
                ReportConstraintUnlessCreated(context, step, created, "a unique constraint");
                break;
            case "AddCheckConstraint":
                ReportConstraintUnlessCreated(context, step, created, "a check constraint");
                break;
            case "AddForeignKey":
                if (IsCreated(step, created, "table"))
                {
                    ReportIfRefusesDeletes(context, step, created);
                }
                else
                {
                    ReportConstraintUnlessCreated(context, step, created, "a foreign key");
                }

                break;
        }
    }

    private static bool AddsRequiredColumnWithoutDefault(IInvocationOperation step)
        => Flag(step, "nullable") is not true
            && Flag(step, "rowVersion") is not true
            && IsAbsent(step, "defaultValue")
            && IsAbsent(step, "defaultValueSql")
            && IsAbsent(step, "computedColumnSql");

    /// <summary>
    /// Says how an <c>AlterColumn</c> narrows the column, or returns null when it only widens it or
    /// changes nothing the previous release depends on. An argument passed a value that isn't a
    /// constant counts as a change, since it can't be compared.
    /// </summary>
    private static string? NarrowingReason(IInvocationOperation step)
    {
        // EF Core always scaffolds the old CLR type; without it the old shape is unknown.
        if (Argument(step, "oldClrType") is not { ArgumentKind: not ArgumentKind.DefaultValue })
        {
            return "it doesn't give the column's old shape (oldClrType and the other old values), so it can't be shown to leave the column usable";
        }

        var type = step.TargetMethod.TypeArguments.FirstOrDefault();
        if (type is not null && Argument(step, "oldClrType")?.Value is ITypeOfOperation oldClrType
            && !SymbolEqualityComparer.Default.Equals(Underlying(type), Underlying(oldClrType.TypeOperand)))
        {
            return "it changes the column's type";
        }

        if (Flag(step, "nullable") is not true && Flag(step, "oldNullable") is not false)
        {
            return "it makes the column NOT NULL, which the previous release may not write";
        }

        if (Flag(step, "nullable") is not false && Flag(step, "oldNullable") is not true)
        {
            return "it lets the column hold NULL, which the previous release may map as required and fail to read";
        }

        if (Flag(step, "nullable") is not true
            && HasDefault(step, "oldDefaultValue", "oldDefaultValueSql")
            && !HasDefault(step, "defaultValue", "defaultValueSql"))
        {
            return "it removes the default from a NOT NULL column, which the previous release's inserts may rely on";
        }

        if (Differs(Text(step, "collation"), OldAnnotation(step, "oldCollation", "Relational:Collation")))
        {
            return "it changes the column's collation, which changes how values compare and can rewrite the table";
        }

        if (Differs(NewAnnotation(step, "MySql:CharSet"), OldAnnotation(step, null, "MySql:CharSet")))
        {
            return "it changes the column's character set";
        }

        var storeType = Text(step, "type");
        var oldStoreType = Text(step, "oldType");
        if (Differs(storeType, oldStoreType) && !IsWiderOfSameType(storeType.Value, oldStoreType.Value))
        {
            return "it changes the column's store type";
        }

        var maxLength = Number(step, "maxLength");
        var oldMaxLength = Number(step, "oldMaxLength");
        if (!maxLength.Known || !oldMaxLength.Known
            || (maxLength.Value is { } length && (oldMaxLength.Value is not { } oldLength || length < oldLength)))
        {
            return "it shortens the column";
        }

        var precision = Number(step, "precision");
        var oldPrecision = Number(step, "oldPrecision");
        var scale = Number(step, "scale");
        var oldScale = Number(step, "oldScale");
        if (!precision.Known || !oldPrecision.Known || !scale.Known || !oldScale.Known
            || precision.Value < oldPrecision.Value || scale.Value < oldScale.Value)
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
    /// A value the step's chained <c>.Annotation(name, value)</c> sets, or a known null when there
    /// is none.
    /// </summary>
    private static (bool Known, string? Value) NewAnnotation(IInvocationOperation step, string annotation)
        => ChainedAnnotation(step, "Annotation", annotation);

    /// <summary>
    /// The column's value before the step, from the <c>old*</c> argument when it is passed, or else
    /// from a chained <c>.OldAnnotation(name, value)</c>. Pomelo's MySQL provider scaffolds the old
    /// collation and character set as annotations.
    /// </summary>
    private static (bool Known, string? Value) OldAnnotation(IInvocationOperation step, string? parameter, string annotation)
        => parameter is not null && Argument(step, parameter) is { ArgumentKind: not ArgumentKind.DefaultValue }
            ? Text(step, parameter)
            : ChainedAnnotation(step, "OldAnnotation", annotation);

    private static (bool Known, string? Value) ChainedAnnotation(IInvocationOperation step, string method, string annotation)
    {
        for (var parent = step.Parent; parent is IInvocationOperation chained; parent = chained.Parent)
        {
            if (chained.TargetMethod.Name == method
                && chained.Arguments.Length == 2
                && chained.Arguments[0].Value.ConstantValue is { HasValue: true, Value: string name }
                && name == annotation)
            {
                var value = Unwrap(chained.Arguments[1].Value);
                return value.ConstantValue is { HasValue: true, Value: null or string }
                    ? (true, (string?)value.ConstantValue.Value)
                    : (false, null);
            }
        }

        return (true, null);
    }

    /// <summary>
    /// True when the new store type holds every value the old one does: the same type with each
    /// size at least as large (<c>nvarchar(100)</c> to <c>nvarchar(200)</c> or <c>nvarchar(max)</c>,
    /// <c>timestamp(3) with time zone</c> to <c>timestamp(6) with time zone</c>, <c>decimal(10,2)</c>
    /// to <c>decimal(18,2)</c> keeping as many digits before the point), or a bounded type moving to
    /// its unbounded form (<c>varchar(100)</c> to <c>longtext</c>, <c>character varying(100)</c> to
    /// <c>text</c>).
    /// </summary>
    private static bool IsWiderOfSameType(string? type, string? oldType)
    {
        if (type is null || oldType is null || !TryParse(oldType, out var oldName, out var oldSizes))
        {
            return false;
        }

        if (UnboundedTypes.TryGetValue(type.Trim(), out var widens) && widens.Contains(oldName, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!TryParse(type, out var name, out var sizes)
            || !string.Equals(name, oldName, StringComparison.OrdinalIgnoreCase)
            || sizes.Length != oldSizes.Length)
        {
            return false;
        }

        if (sizes.Length == 2)
        {
            // Precision and scale: neither may shrink, nor may the digits before the point.
            return sizes[0] >= oldSizes[0] && sizes[1] >= oldSizes[1] && sizes[0] - sizes[1] >= oldSizes[0] - oldSizes[1];
        }

        return sizes.Length == 1 && sizes[0] >= oldSizes[0];
    }

    /// <summary>
    /// Splits a store type into its name (the text around the parentheses, so
    /// <c>timestamp with time zone</c> for <c>timestamp(6) with time zone</c>) and its sizes,
    /// <c>max</c> being the largest.
    /// </summary>
    private static bool TryParse(string storeType, out string name, out long[] sizes)
    {
        name = storeType.Trim();
        sizes = [];
        var open = storeType.IndexOf('(');
        var close = open < 0 ? -1 : storeType.IndexOf(')', open);
        if (open < 0 || close < 0)
        {
            return true;
        }

        name = (storeType.Substring(0, open).Trim() + " " + storeType.Substring(close + 1).Trim()).Trim();
        var parts = storeType.Substring(open + 1, close - open - 1).Split(',');
        sizes = new long[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            if (string.Equals(part, "max", StringComparison.OrdinalIgnoreCase))
            {
                sizes[i] = long.MaxValue;
            }
            else if (!long.TryParse(part, out sizes[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsCreated(IInvocationOperation step, HashSet<string> created, string tableParameter, string schemaParameter = "schema")
        => Text(step, tableParameter) is { Known: true, Value: { } name } && created.Contains(TableKey(Text(step, schemaParameter).Value, name));

    private static void Forget(IInvocationOperation step, HashSet<string> created)
    {
        if (Text(step, "name") is { Known: true, Value: { } name })
        {
            created.Remove(TableKey(Text(step, "schema").Value, name));
        }
    }

    private static void ReportUnlessCreated(
        OperationBlockAnalysisContext context,
        IInvocationOperation step,
        HashSet<string> created,
        string tableParameter,
        DiagnosticDescriptor descriptor,
        string reason)
    {
        if (!IsCreated(step, created, tableParameter))
        {
            Report(context, step, descriptor, reason);
        }
    }

    /// <summary>
    /// A foreign key on a table created in this <c>Up</c> still binds the table it refers to. When
    /// that table already exists and the delete neither cascades nor clears the reference, the
    /// previous release, which knows nothing of the new table, can't delete a row it refers to.
    /// </summary>
    private static void ReportIfRefusesDeletes(OperationBlockAnalysisContext context, IInvocationOperation foreignKey, HashSet<string> created)
    {
        var refusesDeletes = Constant(foreignKey, "onDelete") is not { HasValue: true, Value: int onDelete }
            || onDelete is ReferentialActionNoAction or ReferentialActionRestrict;
        if (refusesDeletes && !IsCreated(foreignKey, created, "principalTable", "principalSchema"))
        {
            Report(context, foreignKey, BreaksPreviousRelease, "the previous release can't delete a row of the table it refers to once the new table holds a reference to it");
        }
    }

    private static void ReportConstraintUnlessCreated(OperationBlockAnalysisContext context, IInvocationOperation step, HashSet<string> created, string constraint)
    {
        if (!IsCreated(step, created, "table"))
        {
            Report(context, step, MayRefuseWrites, constraint, Text(step, "table").Value is { } name ? $"'{name}'" : "the table");
        }
    }

    private static void Report(OperationBlockAnalysisContext context, IInvocationOperation step, DiagnosticDescriptor descriptor, params string[] details)
    {
        // The step's name alone, without a generic call's type argument.
        var location = step.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member }
            ? member.Name.Identifier.GetLocation()
            : step.Syntax.GetLocation();
        context.ReportDiagnostic(Diagnostic.Create(descriptor, location, [step.TargetMethod.Name, .. details]));
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

    private static IOperation Unwrap(IOperation value)
    {
        while (value is IConversionOperation conversion && !value.ConstantValue.HasValue)
        {
            value = conversion.Operand;
        }

        return value;
    }

    /// <summary>
    /// The argument's constant value. A left-out argument is a known null; an argument passed a
    /// value that isn't a constant has no value.
    /// </summary>
    private static Optional<object?> Constant(IInvocationOperation step, string parameter)
    {
        var argument = Argument(step, parameter);
        if (argument is null)
        {
            return new Optional<object?>(null);
        }

        // A left-out optional argument takes its parameter's declared default.
        if (argument.ArgumentKind == ArgumentKind.DefaultValue && argument.Parameter is { HasExplicitDefaultValue: true } declared)
        {
            return new Optional<object?>(declared.ExplicitDefaultValue);
        }

        return Unwrap(argument.Value).ConstantValue;
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

    private static bool HasDefault(IInvocationOperation step, string valueParameter, string sqlParameter)
        => !IsAbsent(step, valueParameter) || !IsAbsent(step, sqlParameter);

    private static bool? Flag(IInvocationOperation step, string parameter)
        => Constant(step, parameter) is { HasValue: true, Value: bool value } ? value : null;

    private static (bool Known, int? Value) Number(IInvocationOperation step, string parameter)
    {
        var constant = Constant(step, parameter);
        return constant.HasValue && constant.Value is null or int ? (true, (int?)constant.Value) : (false, null);
    }

    private static (bool Known, string? Value) Text(IInvocationOperation step, string parameter)
    {
        var constant = Constant(step, parameter);
        return constant.HasValue && constant.Value is null or string ? (true, (string?)constant.Value) : (false, null);
    }

    private static bool Differs((bool Known, string? Value) value, (bool Known, string? Value) old)
        => !value.Known || !old.Known || !string.Equals(value.Value, old.Value, StringComparison.Ordinal);
}
