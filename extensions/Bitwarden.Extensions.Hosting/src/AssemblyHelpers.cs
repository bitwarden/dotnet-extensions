using System.Reflection;

namespace Bitwarden.Extensions.Hosting;

/// <summary>
/// Helper class for working with assembly attributes.
/// </summary>
public static class AssemblyHelpers
{
    private const string _gitHashAssemblyKey = "GitHash";

    private static readonly IEnumerable<AssemblyMetadataAttribute> _assemblyMetadataAttributes;
    private static readonly AssemblyInformationalVersionAttribute? _assemblyInformationalVersionAttributes;
    private static string? _version;
    private static string? _gitHash;

    static AssemblyHelpers()
    {
        _assemblyMetadataAttributes = Assembly.GetEntryAssembly()!
            .GetCustomAttributes<AssemblyMetadataAttribute>();
        _assemblyInformationalVersionAttributes = Assembly.GetEntryAssembly()!
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
    }

    /// <summary>
    /// Gets the version of the entry assembly.
    /// </summary>
    /// <returns></returns>
    public static string? GetVersion()
    {
        if (string.IsNullOrWhiteSpace(_version))
        {
            _version = _assemblyInformationalVersionAttributes?.InformationalVersion;
        }

        return _version;
    }

    /// <summary>
    /// Gets the Git hash of the entry assembly.
    /// </summary>
    /// <returns></returns>
    public static string? GetGitHash()
    {
        if (string.IsNullOrWhiteSpace(_gitHash))
        {
            try
            {
                _gitHash = _assemblyMetadataAttributes.First(i =>
                    i.Key == _gitHashAssemblyKey).Value;
            }
            catch (Exception)
            {
                // suppress
            }
        }

        return _gitHash;
    }
}
