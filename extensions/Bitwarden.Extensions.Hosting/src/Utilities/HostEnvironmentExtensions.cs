using System.Reflection;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Extensions.Hosting.Utilities;

internal static class HostEnvironmentExtensions
{
    public static VersionInfo? GetVersionInfo(this IHostEnvironment hostEnvironment)
    {
        try
        {
            var appAssembly = Assembly.Load(new AssemblyName(hostEnvironment.ApplicationName));
            var assemblyInformationalVersionAttribute = appAssembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

            if (assemblyInformationalVersionAttribute == null)
            {
                return null;
            }

            if (!VersionInfo.TryParse(assemblyInformationalVersionAttribute.InformationalVersion, null, out var versionInfo))
            {
                return null;
            }

            return versionInfo;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
