using System.Reflection;
using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// The <c>fields[type]</c> query parameter: a sparse fieldset, the attributes of a resource a caller
/// wants back.
/// </summary>
/// <typeparam name="T">The resource the fieldset is for; the parameter is named after its type.</typeparam>
/// <remarks>
/// <para>
/// The standards' sparse fieldsets, as JSON:API defines them: <c>fields[account]=email,name</c> asks
/// for those two attributes of each <c>account</c> and no others. An operation that offers one
/// declares it as an ordinary parameter, and hands it to the response, which leaves out every
/// attribute not asked for:
/// </para>
/// <code>
/// public async Task&lt;ResourceDocument&lt;Account&gt;&gt; GetAccount(
///     Fields&lt;Account&gt; fields = default,
///     CancellationToken cancellationToken = default) =>
///     ReadOneResponse(await accounts.GetAsync(fields, cancellationToken), fields);
/// </code>
/// <para>
/// The reason to offer one is an attribute that costs something to work out. The operation asks
/// <see cref="Includes"/> before working it out, so a caller who leaves it out of the fieldset
/// does not pay for it. <c>id</c> and <c>type</c> are always sent, as JSON:API requires.
/// </para>
/// <para>
/// <c>default</c>, and a request that sends no fieldset, is every attribute: the response is what
/// it was before the operation offered one. A fieldset only ever takes attributes away, as JSON:API
/// intends; nothing is withheld from a caller who does not know to ask, so a caller that never sends
/// one is never surprised by a missing attribute. A name that is not one of the resource's attributes is
/// refused with a <c>422</c> at the parameter, rather than quietly ignored, so a caller who
/// misspells a field learns it instead of receiving nothing for it.
/// </para>
/// </remarks>
public readonly record struct Fields<T> where T : Resource
{
    /// <summary>
    /// The query parameter it is read from: <c>fields[</c>, the resource's type, <c>]</c>.
    /// </summary>
    public static string Parameter { get; } = $"fields[{ResourceTypes.For(typeof(T))}]";

    /// <summary>
    /// The attributes a caller may name: every published attribute of the resource.
    /// </summary>
    public static IReadOnlySet<string> Attributes { get; } = typeof(T)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.Name is not (nameof(Resource.Id) or nameof(Resource.Type))
            && !property.IsDefined(typeof(JsonIgnoreAttribute), inherit: true))
        .Select(StoredFields.Published)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// A fieldset naming these attributes.
    /// </summary>
    /// <param name="names">The attributes, by the names a caller sends.</param>
    /// <exception cref="InvalidFieldsException">A name is not one of the resource's attributes.</exception>
    public Fields(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var named = names.ToHashSet(StringComparer.Ordinal);

        if (named.FirstOrDefault(name => !Attributes.Contains(name)) is { } unknown)
        {
            throw new InvalidFieldsException(Parameter, unknown);
        }

        Names = named;
    }

    /// <summary>
    /// The attributes asked for, or null when the caller asked for every one.
    /// </summary>
    public IReadOnlySet<string>? Names { get; }

    /// <summary>
    /// Whether the response carries this attribute: true when no fieldset was sent.
    /// </summary>
    /// <param name="attribute">The attribute, by the name a caller sends.</param>
    public bool Includes(string attribute) => Names?.Contains(attribute) ?? true;

    /// <inheritdoc />
    public override string ToString() => Names is null ? string.Empty : string.Join(',', Names);

    /// <summary>
    /// Binds a handler's parameter.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="parameter">The handler's parameter.</param>
    /// <returns>The parameter.</returns>
    public static ValueTask<Fields<T>> BindAsync(HttpContext context, ParameterInfo parameter) =>
        ValueTask.FromResult((Fields<T>)FieldsBinding.Bind(typeof(Fields<T>), context.Request.Query));
}
