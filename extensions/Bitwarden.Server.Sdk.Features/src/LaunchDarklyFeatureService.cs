using System.Text.Json.Nodes;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Server.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Features;

internal sealed class LaunchDarklyFeatureService : IFeatureService
{
    private readonly ILdClient _ldClient;
    private readonly IContextBuilder _contextBuilder;
    private readonly IOptionsMonitor<FeatureFlagOptions> _featureFlagOptions;
    private readonly ILogger<LaunchDarklyFeatureService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    // Should not change during the course of a request, so cache this
    private Context? _context;

    public LaunchDarklyFeatureService(
        ILaunchDarklyClientProvider launchDarklyClientProvider,
        IContextBuilder contextBuilder,
        IOptionsMonitor<FeatureFlagOptions> featureFlagOptions,
        ILogger<LaunchDarklyFeatureService> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(launchDarklyClientProvider);
        ArgumentNullException.ThrowIfNull(featureFlagOptions);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(httpContextAccessor);

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
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsEnabled(string key, bool defaultValue = false)
    {
        var value = _ldClient.BoolVariation(key, GetContext(), defaultValue);
        AddFeatureFlagTag(key, value);
        return value;
    }

    public int GetIntVariation(string key, int defaultValue = 0)
    {
        var value = _ldClient.IntVariation(key, GetContext(), defaultValue);
        AddFeatureFlagTag(key, value);
        return value;
    }

    public string GetStringVariation(string key, string? defaultValue = null)
    {
        var value = _ldClient.StringVariation(key, GetContext(), defaultValue);
        AddFeatureFlagTag(key, value);
        return value;
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

    private void AddFeatureFlagTag(string key, object value)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return;
        }

        var tagKey = $"feature.flag.{key}";

        // Add to activity (tracing)
        httpContext.Features.Get<IHttpActivityFeature>()?.Activity?.AddTag(tagKey, value);

        // Add to metrics
        httpContext.Features.Get<IHttpMetricsTagsFeature>()?.Tags.Add(new KeyValuePair<string, object?>(tagKey, value));
    }
}
