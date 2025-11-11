using System.Diagnostics.CodeAnalysis;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Server;
using LaunchDarkly.Sdk.Server.Integrations;
using LaunchDarkly.Sdk.Server.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Features;

internal interface ILaunchDarklyClientProvider
{
    ILdClient Get();
}

internal sealed class LaunchDarklyClientProvider : ILaunchDarklyClientProvider, IDisposable
{
    // This class exists to avoid registering ILdClient directly into DI. If LdClient were a singleton then
    // it would not be able to react to options changes but if it is registered as scoped then the DI sees that
    // it is disposable and will dispose it after every scope.

    private readonly ILoggerFactory _loggerFactory;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IVersionInfoAccessor _versionInfoAccessor;
    private readonly IDisposable? _changeToken;

    private LdClient _client;

    public LaunchDarklyClientProvider(
        IOptionsMonitor<FeatureFlagOptions> featureFlagOptions,
        ILoggerFactory loggerFactory,
        IHostEnvironment hostEnvironment,
        IVersionInfoAccessor versionInfoAccessor)
    {
        _loggerFactory = loggerFactory;
        _hostEnvironment = hostEnvironment;
        _versionInfoAccessor = versionInfoAccessor;

        BuildClient(featureFlagOptions.CurrentValue);
        // Subscribe to options changes.
        _changeToken = featureFlagOptions.OnChange(BuildClient);
    }

    [MemberNotNull(nameof(_client))]
    private void BuildClient(FeatureFlagOptions featureFlagOptions)
    {
        var applicationInfo = Components.ApplicationInfo()
            .ApplicationId(_hostEnvironment.ApplicationName)
            .ApplicationName(_hostEnvironment.ApplicationName);

        var versionInfo = _versionInfoAccessor.Get();

        if (versionInfo is not null)
        {
            applicationInfo.ApplicationVersion(versionInfo.GitHash ?? versionInfo.Version.ToString())
                .ApplicationVersionName(versionInfo.Version.ToString());
        }

        var builder = Configuration.Builder(featureFlagOptions.LaunchDarkly.SdkKey)
            .Logging(Components.Logging().Adapter(Logs.CoreLogging(_loggerFactory)))
            .ApplicationInfo(applicationInfo);

        if (string.IsNullOrEmpty(featureFlagOptions.LaunchDarkly.SdkKey))
        {
            builder.DataSource(BuildDataSource(featureFlagOptions.FlagValues))
                .Events(Components.NoEvents);
        }

        var previousClient = Interlocked.Exchange(ref _client, new LdClient(builder.Build()));
        previousClient?.Dispose();
    }

    private static TestData BuildDataSource(Dictionary<string, string> data)
    {
        // TODO: We could support updating just the test data source with
        // changes from the OnChange of options, we currently support it through creating
        // a whole new client but that could be pretty heavy just for flag
        // value changes.
        var source = TestData.DataSource();

        foreach (var (key, value) in data)
        {
            var flag = source.Flag(key);
            var valueSpan = value.AsSpan();
            if (bool.TryParse(valueSpan, out var boolValue))
            {
                flag.ValueForAll(LdValue.Of(boolValue));
            }
            else if (int.TryParse(valueSpan, out var intValue))
            {
                flag.ValueForAll(LdValue.Of(intValue));
            }
            else
            {
                flag.ValueForAll(LdValue.Of(value));
            }

            source.Update(flag);
        }

        return source;
    }

    public ILdClient Get()
    {
        return _client;
    }

    public void Dispose()
    {
        _changeToken?.Dispose();
        _client.Dispose();
    }
}
