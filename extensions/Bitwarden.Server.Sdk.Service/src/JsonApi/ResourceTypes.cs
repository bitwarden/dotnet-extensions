using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// Derives a resource's <c>type</c> from its .NET type name.
/// </summary>
/// <remarks>
/// The API standards require <c>type</c> to be the <em>singular</em> name of the resource, in
/// lowercase kebab-case (<c>user</c>, <c>api-key</c>) even though the path that carries it is
/// plural (<c>/api/v1/users</c>). So the convention is simply the record's name in kebab-case, and
/// no pluralisation is applied. Where the record's name is not the wire name,
/// <see cref="ResourceTypeAttribute"/> overrides it.
/// </remarks>
internal static class ResourceTypes
{
    private static readonly ConcurrentDictionary<Type, string> Cache = new();

    /// <summary>
    /// Returns the resource type for <paramref name="resourceType"/>.
    /// </summary>
    public static string For(Type resourceType) => Cache.GetOrAdd(resourceType, Derive);

    private static string Derive(Type resourceType)
    {
        var declared = resourceType.GetCustomAttribute<ResourceTypeAttribute>()?.Type;
        return string.IsNullOrWhiteSpace(declared) ? KebabCase(resourceType.Name) : declared;
    }

    /// <summary>
    /// Turns <c>ApiKey</c> into <c>api-key</c>, leaving acronym runs intact.
    /// </summary>
    /// <remarks>
    /// The one conversion for every name the platform derives for the wire: a resource's
    /// <c>type</c> and an action's path segment. A name already in kebab case is returned as it is,
    /// so <c>send-email</c> and <c>sendEmail</c> both give <c>send-email</c>.
    /// </remarks>
    internal static string KebabCase(string name)
    {
        var builder = new StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];

            // A boundary sits before an upper-case letter that follows a lower-case one or a digit
            // ("ApiKey"), and before the last upper-case letter of a run that begins a new word
            // ("SsoConfig" rather than "s-s-o-config").
            var followsLower = i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));
            var endsAcronym = i > 0 && i + 1 < name.Length
                && char.IsUpper(name[i - 1]) && char.IsUpper(current) && char.IsLower(name[i + 1]);

            if (char.IsUpper(current) && (followsLower || endsAcronym))
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }
}
