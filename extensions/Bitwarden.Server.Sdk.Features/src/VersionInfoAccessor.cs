using System.Reflection;
using Bitwarden.Server.Sdk.Features;
using Bitwarden.Server.Sdk.Utilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal interface IVersionInfoAccessor
{
    VersionInfo? Get();
}

internal class VersionInfoAccessor : IVersionInfoAccessor
{
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<VersionInfoAccessor> _logger;

    public VersionInfoAccessor(IHostEnvironment hostEnvironment, ILogger<VersionInfoAccessor> logger)
    {
        ArgumentNullException.ThrowIfNull(hostEnvironment);
        ArgumentNullException.ThrowIfNull(logger);

        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    private bool _retrievedVersion;
    private VersionInfo? _cachedVersion;

    public VersionInfo? Get()
    {
        if (_retrievedVersion)
        {
            return _cachedVersion;
        }

        _cachedVersion ??= GetCore();
        _retrievedVersion = true;

        return _cachedVersion;
    }

    private VersionInfo? GetCore()
    {
        try
        {
            var appAssembly = Assembly.Load(new AssemblyName(_hostEnvironment.ApplicationName));
            var assemblyInformationalVersionAttribute = appAssembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

            if (assemblyInformationalVersionAttribute == null)
            {
                _logger.LogMissingVersionAttribute(_hostEnvironment.ApplicationName);
                return null;
            }

            if (!VersionInfo.TryParse(assemblyInformationalVersionAttribute.InformationalVersion, null, out var versionInfo))
            {
                _logger.LogInvalidVersion(assemblyInformationalVersionAttribute.InformationalVersion);
                return null;
            }

            return versionInfo;
        }
        catch (FileNotFoundException)
        {
            _logger.LogNoAssemblyFound(_hostEnvironment.ApplicationName);
            return null;
        }
    }
}
