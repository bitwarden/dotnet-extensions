using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis.Usage;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Diagnostics;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis;

/// <summary>
/// Per-compilation coordinator. It builds the collaborators once, routes each analyzer callback to
/// the one that owns that job, and reports at compilation end. It makes no classification decisions of its
/// own.
/// </summary>
internal sealed class CompilationCoordinator
{
    private readonly string? _repoRoot;
    private readonly string _project;
    private readonly bool _observe;

    private readonly BudgetIndex _budgets;
    private readonly RestrictedTypeIndex _restrictedTypes;
    private readonly DependencyExceptionTracker _exceptions;
    private readonly DependencyUsageLedger _ledger = new();
    private readonly DeclaredMemberSetChecker _sealedMemberSets;
    private readonly BudgetReconciler _reconciler;
    private readonly RestrictedDependencyUseClassifier _classifier;
    private readonly List<Diagnostic> _startupDiagnostics = [];
    private readonly bool _baselinesUnreadable;

    /// <summary>
    /// Whether this compilation declared a <c>[RestrictedDependency]</c> type, which is what tells
    /// a project that owns a restricted type apart from one that merely consumes it. Written from
    /// the concurrent symbol callbacks and read once at compilation end.
    /// </summary>
    private volatile bool _sawAttributedType;

    /// <summary>
    /// A build that names no seed types is the compiler hosting, so the restricted types are
    /// whichever ones have a committed baseline and every rule reports. A seed means a baseline
    /// tool is hosting: it has already discovered the types by attribute, and it cannot treat the
    /// baseline it is rebuilding as the authority, so nothing is enforced and everything observed
    /// leaves through BW0017.
    ///
    /// Observe mode therefore needs both halves of that wiring, not just the seed: a tool that
    /// reads BW0017 must have enabled it through <c>CompilationOptions.WithSpecificDiagnosticOptions</c>,
    /// which no .editorconfig can reach. A seed on its own leaves the build enforcing and is
    /// reported, so the seed key cannot be used to stand a project down.
    /// </summary>
    public CompilationCoordinator(Compilation compilation, AnalyzerOptions options, CancellationToken cancellationToken)
    {
        var globalOptions = options.AnalyzerConfigOptionsProvider.GlobalOptions;
        var seedTypes = RestrictedDependencyConfig.GetSeedTypes(globalOptions);
        var updateInstruction = RestrictedDependencyConfig.GetUpdateInstruction(globalOptions);

        _repoRoot = RestrictedDependencyConfig.GetRepoRoot(globalOptions);
        _project = compilation.AssemblyName ?? "unknown";

        var observationsEnabled = compilation.Options.SpecificDiagnosticOptions
                .TryGetValue(ObservationConstants.DiagnosticId, out var observationReport)
            && observationReport is not (ReportDiagnostic.Suppress or ReportDiagnostic.Default);
        _observe = seedTypes is not null && observationsEnabled;

        var enforcing = !_observe;
        if (seedTypes is not null && !observationsEnabled)
        {
            StartupProblem(
                $"'{AnalyzerConfigConstants.SeedTypes}' names seed types but {ObservationConstants.DiagnosticId} is not enabled through CompilationOptions.WithSpecificDiagnosticOptions, which only a baseline tool can do; this build stays enforcing.",
                Location.None);
        }

        _startupDiagnostics.AddRange(DiagnosticSeverityScanner.Run(compilation, _repoRoot, cancellationToken));

        _budgets = BudgetIndex.Load(options, _repoRoot, cancellationToken, out var baselineProblems);
        _baselinesUnreadable = !baselineProblems.IsEmpty;
        foreach (var problem in baselineProblems)
        {
            StartupProblem(problem, Location.None);
        }

        // The mode decides the seed, not the key: a build left enforcing by a seed it could not
        // honour must still gate against the committed baselines.
        _restrictedTypes = RestrictedTypeIndex.Resolve(compilation, _observe ? seedTypes!.Value : _budgets.Types);
        foreach (var name in _restrictedTypes.NamesWithoutAttribute)
        {
            // A name with a baseline behind it but no attribute is an orphan. A name without one
            // was supplied by a baseline tool, which discovers types by attribute and so has
            // nothing orphaned to report.
            if (_budgets.TryGetPath(name, out var orphanedPath))
            {
                StartupProblem($"Baseline '{orphanedPath}' is orphaned: '{name}' no longer carries [RestrictedDependency]. Delete the file or restore the attribute.", Location.None);
            }
        }

        foreach (var (message, location) in _restrictedTypes.Problems)
        {
            StartupProblem(message, location);
        }

        _exceptions = new DependencyExceptionTracker(DateTime.UtcNow.Date);
        _sealedMemberSets = new DeclaredMemberSetChecker(_budgets, _restrictedTypes, enforcing, updateInstruction);
        _reconciler = new BudgetReconciler(compilation, _budgets, _restrictedTypes, _ledger, _project, updateInstruction);
        _classifier = new RestrictedDependencyUseClassifier(_restrictedTypes, _exceptions, _ledger, _repoRoot, reportForbidden: enforcing);

        _startupDiagnostics.AddRange(DiagnosticSuppressionScanner.OnSymbol(compilation.Assembly, _repoRoot));
        _startupDiagnostics.AddRange(DiagnosticSuppressionScanner.OnSymbol(compilation.SourceModule, _repoRoot));
    }

    public void OnNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        var report = context.ReportDiagnostic;

        Inspect(report, type);

        if (RestrictedTypeModel.FindAttribute(type) is not null)
        {
            _sawAttributedType = true;
            Report(report, _sealedMemberSets.Check(type));
        }

        if (!_classifier.HasRestrictedTypes)
        {
            return;
        }

        _classifier.OnBaseType(type, report);
        _classifier.OnDelegateSignature(type, report);

        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.IsImplicitlyDeclared || !constructor.Locations.Any(l => l.IsInSource))
            {
                continue;
            }

            Inspect(report, constructor);
            foreach (var parameter in constructor.Parameters)
            {
                _classifier.OnConstructorParameter(parameter, constructor, report);
            }
        }
    }

    public void OnMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        // An instance constructor is inspected by OnNamedType, which has the type in hand. A
        // static constructor has no such route, so it is inspected here like any other method:
        // both a suppression on it and an exception attribute on it are honoured.
        if (method.IsImplicitlyDeclared || method.MethodKind is MethodKind.Constructor)
        {
            return;
        }

        var report = context.ReportDiagnostic;
        if (method.AssociatedSymbol is not null)
        {
            // Signature and exceptions belong to the property or event, which OnProperty and OnEvent
            // inspect. Only a suppression placed on the accessor itself is checked here.
            Report(report, DiagnosticSuppressionScanner.OnSymbol(method, _repoRoot));
            return;
        }

        Inspect(report, method);

        if (!_classifier.HasRestrictedTypes)
        {
            return;
        }

        _classifier.OnSignature(method.ReturnType, method, SymbolFacts.SourceLocation(method), method, report);
        foreach (var parameter in method.Parameters)
        {
            _classifier.OnSignature(parameter.Type, method, SymbolFacts.SourceLocation(parameter), parameter, report);
        }
    }

    public void OnProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;
        var report = context.ReportDiagnostic;
        Inspect(report, property);
        if (_classifier.HasRestrictedTypes && property.DeclaredAccessibility != Accessibility.Private && !property.IsImplicitlyDeclared)
        {
            _classifier.OnSignature(property.Type, property, SymbolFacts.SourceLocation(property), property, report);
        }
    }

    public void OnField(SymbolAnalysisContext context)
    {
        var field = (IFieldSymbol)context.Symbol;
        var report = context.ReportDiagnostic;

        // A private backing field is storage, not a site, so only suppressions are checked here.
        Report(report, DiagnosticSuppressionScanner.OnSymbol(field, _repoRoot));
        if (_classifier.HasRestrictedTypes && field.DeclaredAccessibility != Accessibility.Private && !field.IsImplicitlyDeclared)
        {
            _classifier.OnSignature(field.Type, field, SymbolFacts.SourceLocation(field), field, report);
        }
    }

    public void OnEvent(SymbolAnalysisContext context)
    {
        var @event = (IEventSymbol)context.Symbol;
        var report = context.ReportDiagnostic;
        Inspect(report, @event);
        if (_classifier.HasRestrictedTypes && @event.DeclaredAccessibility != Accessibility.Private)
        {
            _classifier.OnSignature(@event.Type, @event, SymbolFacts.SourceLocation(@event), @event, report);
        }
    }

    public void OnOperation(OperationAnalysisContext context)
    {
        if (_classifier.HasRestrictedTypes)
        {
            _classifier.OnOperation(context.Operation, context.ContainingSymbol, context.ReportDiagnostic);
        }
    }

    /// <summary>
    /// A local function is the one declared thing no symbol callback reaches, so its suppressions
    /// are scanned from its operation and deliberately nothing else: an exception attribute on a
    /// local function is never honoured — <see cref="SymbolFacts.NearestMember"/> skips past it —
    /// so validating one here would report on an attribute the gate ignores.
    /// </summary>
    public void OnLocalFunction(OperationAnalysisContext context) =>
        Report(
            context.ReportDiagnostic,
            DiagnosticSuppressionScanner.OnSymbol(((ILocalFunctionOperation)context.Operation).Symbol, _repoRoot));

    public void OnSyntaxTree(SyntaxTreeAnalysisContext context) =>
        Report(context.ReportDiagnostic, DiagnosticSuppressionScanner.OnSyntaxTree(context.Tree, _repoRoot, context.CancellationToken));

    public void OnCompilationEnd(CompilationAnalysisContext context)
    {
        var report = context.ReportDiagnostic;
        Report(report, _startupDiagnostics);

        if (!_observe)
        {
            Report(report, NoBaselineSupplied());
            Report(report, _reconciler.ExcessUses());
            Report(report, _reconciler.StaleEntries());
            return;
        }

        Report(report, _ledger.Observations());
        Report(report, _restrictedTypes.Observations());
        Report(report, _exceptions.Observations());
        foreach (var memberSet in _sealedMemberSets.Observed)
        {
            foreach (var member in memberSet.DeclaredMembers)
            {
                report(ObservationDiagnosticFactory.DeclaredMember(memberSet.Type, member));
            }
        }
    }

    /// <summary>
    /// An enforcing project with analysis on and nothing to enforce against, which is a gate that
    /// looks closed and is open. Reported once, and only when no other BW0015 already says why:
    /// <see cref="BudgetIndex.Load"/> describes a baseline it could not read, and
    /// <see cref="DeclaredMemberSetChecker.Check"/> reports per restricted type this compilation
    /// declares.
    /// </summary>
    private IEnumerable<Diagnostic> NoBaselineSupplied()
    {
        if (!_budgets.Types.IsEmpty || _baselinesUnreadable || _sawAttributedType)
        {
            yield break;
        }

        yield return Diagnostic.Create(
            DiagnosticDescriptors.AttributeInvalid,
            Location.None,
            $"Project '{_project}' has RestrictedDependencyAnalysis enabled but no baseline was supplied, so nothing is gated; set RestrictedDependencyBaselinesPath for this project, or turn RestrictedDependencyAnalysis off for it.");
    }

    /// <summary>
    /// The three checks every declared symbol gets, whatever its kind: a suppression naming one of
    /// the family's ids, the validity of any exception attribute on it, and a
    /// <c>[RestrictedDependency]</c> that cannot mean anything where it sits.
    /// </summary>
    private void Inspect(Action<Diagnostic> report, ISymbol symbol)
    {
        Report(report, DiagnosticSuppressionScanner.OnSymbol(symbol, _repoRoot));
        Report(report, _exceptions.Validate(symbol, reportExpiry: !_observe));

        // Member attributes are read only off a type that carries the attribute itself
        // (RestrictedTypeModel.TryRead), so one on an unmarked type's member governs nothing.
        if (symbol is not INamedTypeSymbol
            && RestrictedTypeModel.FindAttribute(symbol) is { } memberAttribute
            && symbol.ContainingType is { } owner
            && RestrictedTypeModel.FindAttribute(owner) is null)
        {
            report(Diagnostic.Create(
                DiagnosticDescriptors.AttributeInvalid,
                memberAttribute.LocationOf(symbol),
                $"'{owner.Name}.{symbol.Name}': [RestrictedDependency] on a member has no effect unless '{owner.Name}' carries it too."));
        }
    }

    private static void Report(Action<Diagnostic> report, IEnumerable<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            report(diagnostic);
        }
    }

    private void StartupProblem(string message, Location location) =>
        _startupDiagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.AttributeInvalid, location, message));
}
