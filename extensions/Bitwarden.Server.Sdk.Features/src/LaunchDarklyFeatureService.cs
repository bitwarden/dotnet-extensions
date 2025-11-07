using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Utilities;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Server;
using LaunchDarkly.Sdk.Server.Integrations;
using LaunchDarkly.Sdk.Server.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Features;

internal sealed class LaunchDarklyFeatureService : IFeatureService
{
    private readonly ILdClient _ldClient;
    private readonly IContextBuilder _contextBuilder;
    private readonly IOptionsMonitor<FeatureFlagOptions> _featureFlagOptions;

    // Should not change during the course of a request, so cache this
    private Context? _context;

    public LaunchDarklyFeatureService(
        ILdClient ldClient,
        IContextBuilder contextEnricher,
        IOptionsMonitor<FeatureFlagOptions> featureFlagOptions,
        ILogger<LaunchDarklyFeatureService> logger)
    {
        ArgumentNullException.ThrowIfNull(ldClient);
        ArgumentNullException.ThrowIfNull(featureFlagOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _ldClient = ldClient;
        _contextBuilder = contextEnricher;
        _featureFlagOptions = featureFlagOptions;
    }

    public bool IsEnabled(string key, bool defaultValue = false)
    {
        return _ldClient.BoolVariation(key, GetContext(), defaultValue);
    }

    public int GetIntVariation(string key, int defaultValue = 0)
    {
        return _ldClient.IntVariation(key, GetContext(), defaultValue);
    }

    public string GetStringVariation(string key, string? defaultValue = null)
    {
        return _ldClient.StringVariation(key, GetContext(), defaultValue);
    }

    public IReadOnlyDictionary<string, JsonValue> GetAll()
    {
        var context = GetContext();
        var flagsState = _ldClient.AllFlagsState(GetContext());

        var flagValues = new Dictionary<string, JsonValue>();

        if (!flagsState.Valid)
        {
            return flagValues;
        }

        foreach (var knownFlag in _featureFlagOptions.CurrentValue.KnownFlags)
        {
            var ldValue = flagsState.GetFlagValueJson(knownFlag);

            switch (ldValue.Type)
            {
                case LdValueType.Bool:
                    flagValues.Add(knownFlag, (JsonValue)ldValue.AsBool);
                    break;
                case LdValueType.Number:
                    flagValues.Add(knownFlag, (JsonValue)ldValue.AsInt);
                    break;
                case LdValueType.String:
                    flagValues.Add(knownFlag, (JsonValue)ldValue.AsString);
                    break;
            }
        }

        return flagValues;
    }

    private Context GetContext()
    {
        return _context ??= _contextBuilder.Build();
    }
}


internal sealed class LaunchDarklyClientProvider
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly VersionInfo _versionInfo;

    private LdClient _client;

    public LaunchDarklyClientProvider(
        IOptionsMonitor<FeatureFlagOptions> featureFlagOptions,
        ILoggerFactory loggerFactory,
        IHostEnvironment hostEnvironment)
    {
        _loggerFactory = loggerFactory;
        _hostEnvironment = hostEnvironment;

        var versionInfo = _hostEnvironment.GetVersionInfo();

        if (versionInfo == null)
        {
            throw new InvalidOperationException("Unable to attain version information for the current application.");
        }

        _versionInfo = versionInfo;

        BuildClient(featureFlagOptions.CurrentValue);
        // Subscribe to options changes.
        featureFlagOptions.OnChange(BuildClient);
    }

    [MemberNotNull(nameof(_client))]
    private void BuildClient(FeatureFlagOptions featureFlagOptions)
    {
        var builder = Configuration.Builder(featureFlagOptions.LaunchDarkly.SdkKey)
            .Logging(Components.Logging().Adapter(Logs.CoreLogging(_loggerFactory)))
            .ApplicationInfo(Components.ApplicationInfo()
                .ApplicationId(_hostEnvironment.ApplicationName)
                .ApplicationName(_hostEnvironment.ApplicationName)
                .ApplicationVersion(_versionInfo.GitHash ?? _versionInfo.Version.ToString())
                .ApplicationVersionName(_versionInfo.Version.ToString())
            )
            .DataSource(BuildDataSource(featureFlagOptions.FlagValues))
            .Events(Components.NoEvents);

        _client?.Dispose();
        _client = new LdClient(builder.Build());
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

    public LdClient Get()
    {
        return _client;
    }
}
