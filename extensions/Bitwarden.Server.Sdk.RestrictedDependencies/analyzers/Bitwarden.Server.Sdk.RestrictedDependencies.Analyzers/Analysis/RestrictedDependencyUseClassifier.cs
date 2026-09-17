using System.Collections.Immutable;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis.Usage;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Diagnostics;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis;

/// <summary>
/// Decides which of the five usage kinds a use of a restricted type takes, and records it.
///
/// This is deliberately one object rather than five. Every kind asks the same restricted-type index the
/// same questions and every kind ends in the same <c>Record</c> path, where AllowedPaths,
/// exceptions and the use rule are applied. Splitting per kind would cut one cohesive decision
/// into five pieces that each need the others' state.
/// </summary>
internal sealed class RestrictedDependencyUseClassifier
{
    private static readonly ImmutableHashSet<string> _locatorMethodNames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "GetService", "GetRequiredService", "GetServices",
        "GetKeyedService", "GetRequiredKeyedService", "GetKeyedServices",
        "CreateInstance", "GetServiceOrCreateInstance");

    private const string ServiceCollectionName = "Microsoft.Extensions.DependencyInjection.IServiceCollection";

    private const string ServiceProviderName = "System.IServiceProvider";

    private const string ActivatorUtilitiesName = "Microsoft.Extensions.DependencyInjection.ActivatorUtilities";

    private readonly RestrictedTypeIndex _restrictedTypes;
    private readonly DependencyExceptionTracker _exceptions;
    private readonly DependencyUsageLedger _ledger;
    private readonly string? _repoRoot;
    private readonly bool _reportForbidden;

    /// <summary>
    /// When <paramref name="reportForbidden"/> is false a forbidden use is still counted but not
    /// reported, which is what a baseline tool wants: it collects sites, it does not enforce.
    /// </summary>
    public RestrictedDependencyUseClassifier(RestrictedTypeIndex restrictedTypes, DependencyExceptionTracker exceptions, DependencyUsageLedger ledger, string? repoRoot, bool reportForbidden)
    {
        _restrictedTypes = restrictedTypes;
        _exceptions = exceptions;
        _ledger = ledger;
        _repoRoot = repoRoot;
        _reportForbidden = reportForbidden;
    }

    /// <summary>
    /// False when this compilation sees no restricted type, so callers can skip every kind
    /// without touching a symbol.
    /// </summary>
    public bool HasRestrictedTypes => !_restrictedTypes.IsEmpty;

    /// <summary>
    /// Deriving from an implementation of a restricted type is a concrete reference.
    /// </summary>
    public void OnBaseType(INamedTypeSymbol type, Action<Diagnostic> report)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || type.BaseType is null)
        {
            return;
        }

        foreach (var model in _restrictedTypes.ImplementedBy(type.BaseType))
        {
            Record(DependencyUsageType.Concrete, model, null, type, SymbolFacts.SourceLocation(type), type.BaseType, report);
        }
    }

    /// <summary>
    /// A delegate's return type and parameters are signature positions like any other.
    /// </summary>
    public void OnDelegateSignature(INamedTypeSymbol type, Action<Diagnostic> report)
    {
        if (type.TypeKind != TypeKind.Delegate || type.DelegateInvokeMethod is not { } invoke)
        {
            return;
        }

        OnSignature(invoke.ReturnType, type, SymbolFacts.SourceLocation(type), type, report);
        foreach (var parameter in invoke.Parameters)
        {
            OnSignature(parameter.Type, type, SymbolFacts.SourceLocation(parameter), parameter, report);
        }
    }

    /// <summary>
    /// A constructor parameter of the restricted type is injection — the one position where a
    /// direct hit is not an escape.
    /// </summary>
    public void OnConstructorParameter(IParameterSymbol parameter, IMethodSymbol constructor, Action<Diagnostic> report) =>
        Classify(parameter.Type, DependencyUsageType.Injection, constructor, SymbolFacts.SourceLocation(parameter), parameter, report);

    /// <summary>
    /// Any other position the type can occupy in a signature or in non-private storage, where a
    /// direct hit means the type escapes its consumer.
    /// </summary>
    public void OnSignature(ITypeSymbol type, ISymbol containing, Location location, ISymbol subject, Action<Diagnostic> report) =>
        Classify(type, DependencyUsageType.Escape, containing, location, subject, report);

    /// <summary>
    /// Uses that only exist in a method body: calls, member references, construction, and
    /// <c>typeof</c>.
    /// </summary>
    public void OnOperation(IOperation operation, ISymbol containing, Action<Diagnostic> report)
    {
        var location = operation.Syntax.GetLocation();
        switch (operation)
        {
            case IInvocationOperation invocation:
                OnInvocation(invocation, containing, location, report);
                break;

            case IPropertyReferenceOperation propertyReference:
                OnMemberReference(propertyReference.Property, propertyReference, containing, location, report);
                break;

            case IMethodReferenceOperation methodReference:
                OnMemberReference(methodReference.Method, methodReference, containing, location, report);
                break;

            case IEventReferenceOperation eventReference:
                OnMemberReference(eventReference.Event, eventReference, containing, location, report);
                break;

            case IFieldReferenceOperation fieldReference:
                OnMemberReference(fieldReference.Field, fieldReference, containing, location, report);
                break;

            case IObjectCreationOperation creation when creation.Type is INamedTypeSymbol created:
                RecordConcreteFromOutside(created, containing, location, report);
                break;

            case ITypeOfOperation typeOf when typeOf.TypeOperand is INamedTypeSymbol operand:
                // A typeof inside an Add*/TryAdd* call or an attribute is registration or metadata,
                // not a use of the implementation.
                if (IsInsideDiRegistration(typeOf) || IsInsideAttribute(typeOf))
                {
                    break;
                }

                RecordConcreteFromOutside(operand, containing, location, report);
                break;
        }
    }

    private void OnInvocation(IInvocationOperation invocation, ISymbol containing, Location location, Action<Diagnostic> report)
    {
        var method = invocation.TargetMethod;

        if (IsServiceLocator(invocation))
        {
            var resolved = false;
            foreach (var candidate in LocatorTypeArguments(invocation))
            {
                if (_restrictedTypes.TryGet(candidate.OriginalDefinition, out var model))
                {
                    Record(DependencyUsageType.Locator, model, null, containing, location, candidate, report);
                    resolved = true;
                }
            }

            if (resolved)
            {
                return;
            }
        }

        if (method.MethodKind == MethodKind.Constructor)
        {
            // Roslyn models `: base(...)` and `: this(...)` identically. Only the first hands the
            // dependency to another class; a `this(...)` chain keeps it inside this one, and the
            // constructor it chains to already counts the parameter as injection.
            if (!SymbolEqualityComparer.Default.Equals(method.ContainingType?.OriginalDefinition, containing.ContainingType?.OriginalDefinition))
            {
                foreach (var argument in invocation.Arguments)
                {
                    var value = argument.Value is IConversionOperation conversion ? conversion.Operand : argument.Value;
                    if (value.Type is INamedTypeSymbol argumentType && _restrictedTypes.TryGet(argumentType.OriginalDefinition, out var model))
                    {
                        Record(DependencyUsageType.Escape, model, null, containing, argument.Syntax.GetLocation(), argument.Parameter ?? (ISymbol)method, report);
                    }
                }
            }

            return;
        }

        OnMemberReference(method, invocation, containing, location, report);

        if (!IsDiRegistration(invocation))
        {
            foreach (var typeArgument in method.TypeArguments.OfType<INamedTypeSymbol>())
            {
                foreach (var model in _restrictedTypes.ImplementedBy(typeArgument))
                {
                    Record(DependencyUsageType.Concrete, model, null, containing, location, typeArgument, report);
                }
            }
        }
    }

    private void OnMemberReference(ISymbol member, IOperation operation, ISymbol containing, Location location, Action<Diagnostic> report)
    {
        // nameof is a compile-time string, not a use.
        if (IsInsideNameOf(operation))
        {
            return;
        }

        if (_restrictedTypes.TryMapMember(member, out var mapped))
        {
            Record(DependencyUsageType.Member, mapped.Model, mapped.Member, containing, location, mapped.Member, report);
            return;
        }

        if (member.IsStatic && member.ContainingType is { } owner)
        {
            RecordConcreteFromOutside(owner, containing, location, report);
        }
    }

    /// <summary>
    /// Records a concrete reference unless the reference comes from inside the referenced type,
    /// which is the type calling itself rather than a consumer reaching for it.
    /// </summary>
    private void RecordConcreteFromOutside(INamedTypeSymbol referenced, ISymbol containing, Location location, Action<Diagnostic> report)
    {
        foreach (var model in _restrictedTypes.ImplementedBy(referenced))
        {
            if (!SymbolFacts.IsWithin(containing, referenced))
            {
                Record(DependencyUsageType.Concrete, model, null, containing, location, referenced, report);
            }
        }
    }

    /// <summary>
    /// Routes one type position. A direct hit on a restricted type records
    /// <paramref name="directHit"/> — the only thing that differs between a constructor parameter
    /// and every other signature position. An implementing type records Concrete, and a restricted
    /// type buried in a generic or array argument records Escape.
    /// </summary>
    private void Classify(ITypeSymbol type, DependencyUsageType directHit, ISymbol containing, Location location, ISymbol subject, Action<Diagnostic> report)
    {
        if (type is ITypeParameterSymbol typeParameter)
        {
            // `Consumer<T>(T service) where T : IUserService` injects the restricted type as
            // surely as naming it does, so each constraint is routed as the position it stands in.
            // Circular constraints are a compile error, so this terminates.
            foreach (var constraint in typeParameter.ConstraintTypes)
            {
                Classify(constraint, directHit, containing, location, subject, report);
            }

            return;
        }

        if (type is INamedTypeSymbol named)
        {
            if (_restrictedTypes.TryGet(named.OriginalDefinition, out var direct))
            {
                Record(directHit, direct, null, containing, location, subject, report);
                return;
            }

            var implemented = _restrictedTypes.ImplementedBy(named);
            if (!implemented.IsEmpty)
            {
                foreach (var model in implemented)
                {
                    Record(DependencyUsageType.Concrete, model, null, containing, location, named, report);
                }

                return;
            }
        }

        foreach (var model in _restrictedTypes.NestedIn(type))
        {
            Record(DependencyUsageType.Escape, model, null, containing, location, subject, report);
        }
    }

    /// <summary>
    /// Applies AllowedPaths, exceptions and the use rule to one observed use, then either reports
    /// it immediately (forbidden) or stores it for the compilation-end count.
    /// </summary>
    private void Record(DependencyUsageType kind, RestrictedTypeModel model, ISymbol? member, ISymbol containing, Location location, ISymbol subject, Action<Diagnostic> report)
    {
        var file = location.SourceTree is null ? string.Empty : RepositoryPathNormalizer.ToRelative(location.SourceTree.FilePath, _repoRoot);
        if (model.IsAllowedPath(file))
        {
            return;
        }

        var memberId = member?.OriginalDefinition.GetDocumentationCommentId();
        var key = new DependencyUsageKey(model.MetadataName, kind, memberId, SymbolFacts.SiteId(containing));

        if (_exceptions.Covers(kind, model.Type, containing))
        {
            _ledger.AddExcepted(key, model, member, subject, file, location);
            return;
        }

        var useRule = model.RuleFor(member);
        if (useRule.IsForbidden && _reportForbidden)
        {
            report(DependencyUsageDiagnosticFactory.Create(kind, model, useRule, member, subject, location));
        }

        _ledger.Add(key, model, member, subject, file, location);
    }

    private static IEnumerable<INamedTypeSymbol> LocatorTypeArguments(IInvocationOperation invocation)
    {
        foreach (var typeArgument in invocation.TargetMethod.TypeArguments.OfType<INamedTypeSymbol>())
        {
            yield return typeArgument;
        }

        foreach (var argument in invocation.Arguments)
        {
            var value = argument.Value is IConversionOperation conversion ? conversion.Operand : argument.Value;
            if (value is ITypeOfOperation { TypeOperand: INamedTypeSymbol operand })
            {
                yield return operand;
            }
        }
    }

    /// <summary>
    /// Whether this call resolves a service from a container. The method names alone are too
    /// common to match on — a repository's own <c>CreateInstance&lt;T&gt;</c> is not a locator —
    /// so the receiver decides, the way <see cref="IsDiRegistration"/> does. Instance, extension
    /// and static forms are all covered, which is also what makes the keyed variants work:
    /// <c>IKeyedServiceProvider</c> derives from <see cref="IServiceProvider"/>.
    /// </summary>
    private static bool IsServiceLocator(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (!_locatorMethodNames.Contains(method.Name))
        {
            return false;
        }

        if (method.ContainingType?.ToDisplayString() == ActivatorUtilitiesName)
        {
            return true;
        }

        var receiver = method.IsExtensionMethod && method.Parameters.Length > 0
            ? method.Parameters[0].Type
            : invocation.Instance?.Type ?? method.ContainingType;
        return IsServiceProvider(receiver);
    }

    private static bool IsServiceProvider(ITypeSymbol? type) =>
        type is not null
        && (type.ToDisplayString() == ServiceProviderName || type.AllInterfaces.Any(i => i.ToDisplayString() == ServiceProviderName));

    private static bool IsDiRegistration(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (!method.Name.StartsWith("Add", StringComparison.Ordinal) && !method.Name.StartsWith("TryAdd", StringComparison.Ordinal))
        {
            return false;
        }

        var receiver = method.IsExtensionMethod && method.Parameters.Length > 0
            ? method.Parameters[0].Type
            : invocation.Instance?.Type ?? method.ContainingType;
        return IsServiceCollection(receiver);
    }

    private static bool IsServiceCollection(ITypeSymbol? type) =>
        type is not null
        && (type.ToDisplayString() == ServiceCollectionName || type.AllInterfaces.Any(i => i.ToDisplayString() == ServiceCollectionName));

    private static bool IsInsideDiRegistration(IOperation operation) =>
        HasAncestor(operation, o => o is IInvocationOperation invocation && IsDiRegistration(invocation));

    private static bool IsInsideAttribute(IOperation operation) =>
        HasAncestor(operation, o => o is IAttributeOperation);

    private static bool IsInsideNameOf(IOperation operation) =>
        HasAncestor(operation, o => o is INameOfOperation);

    private static bool HasAncestor(IOperation operation, Func<IOperation, bool> predicate)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            if (predicate(current))
            {
                return true;
            }
        }

        return false;
    }
}
