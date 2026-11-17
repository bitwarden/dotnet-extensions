using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Reads a cached resource object into its generated type, leniently.
/// </summary>
public static class CachedResources
{
    private static Newtonsoft.Json.JsonSerializerSettings? _lenient;

    /// <summary>
    /// The resource object as <typeparamref name="T"/>, or null when it will not read.
    /// </summary>
    /// <typeparam name="T">The generated resource type.</typeparam>
    /// <param name="resource">The cached resource object.</param>
    /// <returns>The resource, or null.</returns>
    [RequiresUnreferencedCode("Newtonsoft reads the generated type by reflection.")]
    [RequiresDynamicCode("Newtonsoft reads the generated type by reflection.")]
    public static T? Read<T>(string resource)
        where T : class
    {
        try
        {
            _lenient ??= new Newtonsoft.Json.JsonSerializerSettings { ContractResolver = new LenientResolver() };

            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(resource, _lenient);
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The generated contract's own resolution, but nothing is required to be present.
    /// </summary>
    [RequiresUnreferencedCode("Newtonsoft reads the generated type by reflection.")]
    [RequiresDynamicCode("Newtonsoft reads the generated type by reflection.")]
    private sealed class LenientResolver : Newtonsoft.Json.Serialization.DefaultContractResolver
    {
        protected override Newtonsoft.Json.Serialization.JsonProperty CreateProperty(
            MemberInfo member,
            Newtonsoft.Json.MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);
            property.Required = Newtonsoft.Json.Required.Default;
            return property;
        }
    }
}
