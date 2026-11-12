using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Builds the value of a sparse fieldset, <c>fields[type]</c>, from a client's generated contract.
/// </summary>
/// <remarks>
/// <para>
/// A fieldset only takes attributes away: a service sends every attribute when none is named. A
/// caller that does not need an attribute which costs the service a read leaves it out, and the
/// safe way to say "everything but that" is every attribute the contract knows, less that one:
/// </para>
/// <code>
/// var fields = Fieldsets.EveryExcept&lt;WidgetAttributes&gt;("usage");   // "name,color,...,version"
/// </code>
/// <para>
/// Listing the wanted attributes by hand would drop any the contract marks required, and the
/// generated client would then fail to read the response.
/// </para>
/// <para>
/// Read off the generated attributes class, so the list is the contract the client was generated
/// from: it cannot name an attribute the service does not publish, and it gains new ones when the
/// client is regenerated. Both serializers' property-name attributes are recognized, by name, so
/// this package depends on neither.
/// </para>
/// </remarks>
public static class Fieldsets
{
    private static readonly ConcurrentDictionary<Type, string> Cache = new();

    /// <summary>
    /// Every attribute the contract publishes, comma separated.
    /// </summary>
    /// <typeparam name="TAttributes">The generated class holding a resource's attributes.</typeparam>
    /// <returns>The fieldset's value.</returns>
    public static string Every<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TAttributes>() =>
        Cache.GetOrAdd(typeof(TAttributes), _ => Names(typeof(TAttributes)));

    /// <summary>
    /// Every attribute the contract publishes but these, comma separated.
    /// </summary>
    /// <typeparam name="TAttributes">The generated class holding a resource's attributes.</typeparam>
    /// <param name="leftOut">The attributes to leave out, by their published names.</param>
    /// <returns>The fieldset's value.</returns>
    /// <exception cref="ArgumentException">An attribute to leave out is not one the contract publishes.</exception>
    public static string EveryExcept<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TAttributes>(
        params string[] leftOut)
    {
        ArgumentNullException.ThrowIfNull(leftOut);

        var every = Every<TAttributes>().Split(',');

        // A name the contract doesn't publish would leave out nothing, silently.
        if (leftOut.FirstOrDefault(name => !every.Contains(name, StringComparer.Ordinal)) is { } unknown)
        {
            throw new ArgumentException($"'{unknown}' is not an attribute of {typeof(TAttributes).Name}.", nameof(leftOut));
        }

        return string.Join(',', every.Where(name => !leftOut.Contains(name, StringComparer.Ordinal)));
    }

    /// <summary>
    /// The attributes a caller reads, checked against the contract.
    /// </summary>
    /// <typeparam name="TAttributes">The generated class holding a resource's attributes.</typeparam>
    /// <param name="wanted">The attributes the caller reads.</param>
    /// <returns>The same names, for a read that should take only these.</returns>
    /// <exception cref="ArgumentException">A name the contract does not publish.</exception>
    /// <remarks>
    /// For asking for little: a read whose every attribute the owning service caches can be answered
    /// from its cache (<see cref="ICachedReads{TClient}"/>). What goes to the service on a miss is
    /// <see cref="ForService{TAttributes}"/> of these, with the attributes the contract requires.
    /// </remarks>
    public static IReadOnlyList<string> Only<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TAttributes>(
        params string[] wanted)
    {
        ArgumentNullException.ThrowIfNull(wanted);

        var every = Every<TAttributes>().Split(',');

        if (wanted.FirstOrDefault(name => !every.Contains(name, StringComparer.Ordinal)) is { } unknown)
        {
            throw new ArgumentException($"'{unknown}' is not an attribute of {typeof(TAttributes).Name}.", nameof(wanted));
        }

        return [.. wanted.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The fieldset to send the service for these attributes: them, and the ones the contract marks
    /// required, which a response must carry for the generated client to read it.
    /// </summary>
    /// <typeparam name="TAttributes">The generated class holding a resource's attributes.</typeparam>
    /// <param name="fields">The attributes the caller reads.</param>
    /// <returns>The fieldset's value, comma separated.</returns>
    public static string ForService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TAttributes>(
        IEnumerable<string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var required = typeof(TAttributes)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(Required)
            .Select(Published)
            .OfType<string>();

        return string.Join(',', fields.Concat(required).Distinct(StringComparer.Ordinal));
    }

    /// <summary>
    /// Whether the contract requires the attribute to be present: Newtonsoft's <c>Required</c>
    /// other than <c>Default</c>, or System.Text.Json's <c>[JsonRequired]</c>.
    /// </summary>
    private static bool Required(PropertyInfo property) => property
        .GetCustomAttributesData()
        .Any(attribute => attribute.AttributeType.Name == "JsonRequiredAttribute"
            || (attribute.AttributeType.Name == "JsonPropertyAttribute"
                && attribute.NamedArguments.Any(named => named.MemberName == "Required"
                    && named.TypedValue.Value is int value && value != 0)));

    private static string Names(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type attributes) =>
        string.Join(',', attributes
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Select(Published)
        .OfType<string>()
        .Distinct(StringComparer.Ordinal));

    /// <remarks>
    /// The name a property is published under: Newtonsoft's <c>JsonProperty("name")</c>, or
    /// System.Text.Json's <c>JsonPropertyName("name")</c>, each of which takes the name as its first
    /// constructor argument. A property with neither is not part of the contract (a generated
    /// class's <c>AdditionalProperties</c>, say) and is left out.
    /// </remarks>
    private static string? Published(PropertyInfo property) => property
        .GetCustomAttributesData()
        .Where(attribute => attribute.AttributeType.Name is "JsonPropertyAttribute" or "JsonPropertyNameAttribute")
        .Select(attribute => attribute.ConstructorArguments.FirstOrDefault().Value as string)
        .FirstOrDefault(name => !string.IsNullOrEmpty(name));
}
