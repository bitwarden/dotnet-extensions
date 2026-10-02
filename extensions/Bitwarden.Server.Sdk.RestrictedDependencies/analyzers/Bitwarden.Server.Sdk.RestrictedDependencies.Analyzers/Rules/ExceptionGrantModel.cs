using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Rules;

/// <summary>
/// One <c>[RestrictedDependencyException]</c> as applied to a class or member, with its validation
/// result. An invalid exception excepts nothing, so a half-written attribute fails closed.
/// </summary>
internal sealed class ExceptionGrantModel
{
    private readonly DateTime? _expiresOn;

    private ExceptionGrantModel(
        INamedTypeSymbol? restrictedType,
        string? owner,
        string? reason,
        string? expires,
        DateTime? expiresOn,
        Location location,
        ImmutableArray<string> problems)
    {
        RestrictedType = restrictedType;
        Owner = owner;
        Reason = reason;
        Expires = expires;
        _expiresOn = expiresOn;
        Location = location;
        Problems = problems;
    }

    public INamedTypeSymbol? RestrictedType { get; }
    public string? Owner { get; }
    public string? Reason { get; }
    public string? Expires { get; }
    public Location Location { get; }

    /// <summary>
    /// Why the attribute is unusable; empty when it is valid.
    /// </summary>
    public ImmutableArray<string> Problems { get; }

    public bool IsValid => Problems.IsEmpty;

    /// <summary>
    /// True when the exception is valid but its date has passed relative to <paramref name="today"/>.
    /// </summary>
    public bool IsExpired(DateTime today) => IsValid && _expiresOn!.Value < today.Date;

    /// <summary>
    /// True when this exception covers uses of <paramref name="type"/>.
    /// </summary>
    public bool Covers(INamedTypeSymbol type) =>
        IsValid && RestrictedType is not null
        && SymbolEqualityComparer.Default.Equals(RestrictedType.OriginalDefinition, type.OriginalDefinition);

    /// <summary>
    /// Reads every <c>[RestrictedDependencyException]</c> on <paramref name="symbol"/>.
    /// </summary>
    public static ImmutableArray<ExceptionGrantModel> ReadAll(ISymbol symbol)
    {
        var result = ImmutableArray.CreateBuilder<ExceptionGrantModel>();
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != AttributeConstants.RestrictedDependencyExceptionAttributeName)
            {
                continue;
            }

            var problems = ImmutableArray.CreateBuilder<string>();
            var restrictedType = attribute.ConstructorArguments.Length == 1
                ? attribute.ConstructorArguments[0].Value as INamedTypeSymbol
                : null;
            if (restrictedType is null)
            {
                problems.Add("the restricted type argument must be a typeof() of a named type");
            }

            var owner = attribute.ReadString("Owner");
            if (owner is null)
            {
                problems.Add("Owner is required");
            }

            var reason = attribute.ReadString("Reason");
            if (reason is null)
            {
                problems.Add("Reason is required");
            }

            var expires = attribute.ReadString("Expires");
            DateTime? expiresOn = null;
            if (expires is null)
            {
                problems.Add("Expires is required");
            }
            else if (DateTime.TryParseExact(expires, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                expiresOn = parsed;
            }
            else
            {
                problems.Add($"Expires '{expires}' is not a yyyy-MM-dd date");
            }

            var location = attribute.LocationOf(symbol);

            result.Add(new ExceptionGrantModel(restrictedType, owner, reason, expires, expiresOn, location, problems.ToImmutable()));
        }

        return result.ToImmutable();
    }

}
