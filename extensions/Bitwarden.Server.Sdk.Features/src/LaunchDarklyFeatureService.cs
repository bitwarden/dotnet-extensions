using System.Text.Json.Nodes;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Server.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Features;

internal sealed class LaunchDarklyFeatureService : IFeatureService
{
    private readonly ILdClient _ldClient;
    private readonly IContextBuilder _contextBuilder;
    private readonly IOptionsMonitor<FeatureFlagOptions> _featureFlagOptions;
    private readonly ILogger<LaunchDarklyFeatureService> _logger;

    // Should not change during the course of a request, so cache this
    private Context? _context;

    public LaunchDarklyFeatureService(
        ILaunchDarklyClientProvider launchDarklyClientProvider,
        IContextBuilder contextBuilder,
        IOptionsMonitor<FeatureFlagOptions> featureFlagOptions,
        ILogger<LaunchDarklyFeatureService> logger)
    {
        ArgumentNullException.ThrowIfNull(launchDarklyClientProvider);
        ArgumentNullException.ThrowIfNull(featureFlagOptions);
        ArgumentNullException.ThrowIfNull(logger);

        // Retrieving the ILdClient once in the constructor does mean that
        // we no longer receive possible changes to the clients during the
        // scope of this service, that's acceptible for now since it only changes
        // due to configuration updates which is only expected to be useful
        // in dev scenarios and getting the feature flag change on the next
        // request into the server is more than enough.
        _ldClient = launchDarklyClientProvider.Get();
        _contextBuilder = contextBuilder;
        _featureFlagOptions = featureFlagOptions;
        _logger = logger;
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
        var flagsState = _ldClient.AllFlagsState(GetContext());

        var flagValues = new Dictionary<string, JsonValue>();

        if (!flagsState.Valid)
        {
            _logger.LogInvalidFlagsState();
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
