using System.Collections.Immutable;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis;
using Microsoft.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Rules;

/// <summary>
/// Everything the analyzer knows about one restricted type, read from its
/// <c>[RestrictedDependency]</c> attribute and the attributes on its members.
/// </summary>
internal sealed class RestrictedTypeModel
{
    private readonly ImmutableDictionary<ISymbol, UseRule> _memberOverrides;

    private RestrictedTypeModel(
        INamedTypeSymbol type,
        UseRule defaultRule,
        ImmutableDictionary<ISymbol, UseRule> memberOverrides,
        bool sealMembers,
        bool sealNestedTypes,
        bool sealProperties,
        ImmutableArray<PathGlobMatcher> allowedPaths,
        ImmutableArray<(string Message, Location Location)> problems)
    {
        Type = type;
        MetadataName = SymbolFacts.MetadataName(type);
        Default = defaultRule;
        _memberOverrides = memberOverrides;
        SealMembers = sealMembers;
        SealNestedTypes = sealNestedTypes;
        SealProperties = sealProperties;
        AllowedPaths = allowedPaths;
        Problems = problems;
    }

    public INamedTypeSymbol Type { get; }

    /// <summary>
    /// Fully qualified metadata name, which is also the baseline's <c>type</c> field.
    /// </summary>
    public string MetadataName { get; }

    public UseRule Default { get; }
    public bool SealMembers { get; }
    public bool SealNestedTypes { get; }
    public bool SealProperties { get; }
    public bool IsSealed => SealMembers || SealNestedTypes || SealProperties;
    public ImmutableArray<PathGlobMatcher> AllowedPaths { get; }

    /// <summary>
    /// Attribute problems to report as BW0015, each with the location of the offending attribute.
    /// </summary>
    public ImmutableArray<(string Message, Location Location)> Problems { get; }

    /// <summary>
    /// Ticket or cluster id for messages; "untracked" when the attribute has none.
    /// </summary>
    public string Tracking => Default.Tracking ?? "untracked";

    /// <summary>
    /// Team that owns the dissolution, for messages and reports; "unowned" when the attribute has
    /// none, matching how <see cref="Tracking"/> renders.
    /// </summary>
    public string Owner => Default.Owner ?? "unowned";

    /// <summary>
    /// The rule that governs a use of <paramref name="member"/>: its own attribute when it has
    /// one, otherwise the type-level default.
    /// </summary>
    public UseRule RuleFor(ISymbol? member) =>
        member is not null && _memberOverrides.TryGetValue(member.OriginalDefinition, out var rule) ? rule : Default;

    /// <summary>
    /// True when a file at this repo-relative path is exempt from every rule for this type.
    /// </summary>
    public bool IsAllowedPath(string relativePath) => AllowedPaths.Any(g => g.IsMatch(relativePath));

    /// <summary>
    /// Reads the model from <paramref name="type"/>. Returns null when the type carries no
    /// <c>[RestrictedDependency]</c> attribute.
    /// </summary>
    public static RestrictedTypeModel? TryRead(INamedTypeSymbol type)
    {
        var attribute = FindAttribute(type);
        if (attribute is null)
        {
            return null;
        }

        var problems = ImmutableArray.CreateBuilder<(string, Location)>();
        var typeLocation = attribute.LocationOf(type);
        var defaultRule = ReadUseRule(attribute, type, typeLocation, problems);

        var allowedPaths = ImmutableArray.CreateBuilder<PathGlobMatcher>();
        foreach (var pattern in attribute.ReadStringArray("AllowedPaths"))
        {
            if (PathGlobMatcher.TryCreate(pattern, out var glob))
            {
                allowedPaths.Add(glob!);
            }
            else
            {
                problems.Add(($"'{type.Name}' has an invalid AllowedPaths glob '{pattern}'; use repo-relative forward-slash paths such as \"src/Core/Services/**\".", typeLocation));
            }
        }

        var overrides = ImmutableDictionary.CreateBuilder<ISymbol, UseRule>(SymbolEqualityComparer.Default);
        foreach (var member in type.GetMembers())
        {
            var memberAttribute = FindAttribute(member);
            if (memberAttribute is null)
            {
                continue;
            }

            var memberLocation = memberAttribute.LocationOf(member);
            overrides[member.OriginalDefinition] = ReadUseRule(memberAttribute, member, memberLocation, problems);
            foreach (var typeOnly in new[] { "SealMembers", "SealNestedTypes", "SealProperties", "AllowedPaths" })
            {
                if (memberAttribute.NamedArguments.Any(a => a.Key == typeOnly))
                {
                    problems.Add(($"'{type.Name}.{member.Name}': {typeOnly} is a type-level setting and has no effect on a member.", memberLocation));
                }
            }
        }

        return new RestrictedTypeModel(
            type,
            defaultRule,
            overrides.ToImmutable(),
            attribute.ReadBool("SealMembers", false),
            attribute.ReadBool("SealNestedTypes", false),
            attribute.ReadBool("SealProperties", false),
            allowedPaths.ToImmutable(),
            problems.ToImmutable());
    }

    /// <summary>
    /// The <c>[RestrictedDependency]</c> attribute on <paramref name="symbol"/>, matched by name so
    /// that every compilation's generated copy of the attribute is recognized.
    /// </summary>
    public static AttributeData? FindAttribute(ISymbol symbol) =>
        symbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == AttributeConstants.RestrictedDependencyAttributeName);

    private static UseRule ReadUseRule(AttributeData attribute, ISymbol target, Location location, ImmutableArray<(string, Location)>.Builder problems)
    {
        var allowExisting = attribute.ReadBool("AllowExistingUses", true);
        var allowNew = attribute.ReadBool("AllowNewUses", false);
        if (!allowExisting && allowNew)
        {
            problems.Add(($"'{target.Name}' combines AllowExistingUses = false with AllowNewUses = true, which has no meaning.", location));
        }

        return new UseRule(
            allowExisting,
            allowNew,
            attribute.ReadString("Tracking"),
            attribute.ReadString("Owner"),
            attribute.ReadString("Replacement"));
    }

}
