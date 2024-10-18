using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Server;
using LaunchDarkly.Sdk.Server.Integrations;
using LaunchDarkly.Sdk.Server.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bitwarden.Extensions.Hosting.Features;

internal sealed class LaunchDarklyFeatureService : IFeatureService
{
    const string AnonymousUser = "25a15cac-58cf-4ac0-ad0f-b17c4bd92294";

    private readonly ILdClient _ldClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOptionsMonitor<FeatureFlagOptions> _featureFlagOptions;

    // Should not change during the course of a request, so cache this
    private Context? _context;

    public LaunchDarklyFeatureService(
        ILdClient ldClient,
        IHttpContextAccessor httpContextAccessor,
        IOptionsMonitor<FeatureFlagOptions> featureFlagOptions)
    {
        ArgumentNullException.ThrowIfNull(ldClient);
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(featureFlagOptions);

        _ldClient = ldClient;
        _httpContextAccessor = httpContextAccessor;
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
        return _context ??= BuildContext();
    }

    private Context BuildContext()
    {
        static void AddCommon(ContextBuilder contextBuilder, HttpContext httpContext)
        {
            if (httpContext.Request.Headers.TryGetValue("bitwarden-client-version", out var clientVersion))
            {
                contextBuilder.Set("client-version", clientVersion);
            }

            if (httpContext.Request.Headers.TryGetValue("device-type", out var deviceType))
            {
                contextBuilder.Set("device-type", deviceType);
            }
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            // Likely a mistake, should we log a warning?
            return Context.Builder(AnonymousUser)
                .Kind(ContextKind.Default)
                .Anonymous(true)
                .Build();
        }

        // TODO: We need to start enforcing this
        var subject = httpContext.User.FindFirstValue("sub");

        if (string.IsNullOrEmpty(subject))
        {
            // Anonymous but with common headers
            var anon = Context.Builder(AnonymousUser)
                .Kind(ContextKind.Default)
                .Anonymous(true);

            AddCommon(anon, httpContext);
            return anon.Build();
        }

        // TODO: Need to start enforcing this
        var organizations = httpContext.User.FindAll("organization");

        var contextBuilder = Context.Builder(subject)
            .Kind(ContextKind.Default) // TODO: This is not right
            .Set("organizations", LdValue.ArrayFrom(organizations.Select(c => LdValue.Of(c.Value))));

        AddCommon(contextBuilder, httpContext);

        return contextBuilder.Build();
    }
}


internal sealed class LaunchDarklyClientProvider
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHostEnvironment _hostEnvironment;

    private LdClient _client;

    public LaunchDarklyClientProvider(
        IOptionsMonitor<FeatureFlagOptions> featureFlagOptions,
        ILoggerFactory loggerFactory,
        IHostEnvironment hostEnvironment)
    {
        _loggerFactory = loggerFactory;
        _hostEnvironment = hostEnvironment;

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
                .ApplicationVersion(AssemblyHelpers.GetGitHash() ?? $"v{AssemblyHelpers.GetVersion()}")
            // .ApplicationVersionName(AssemblyHelpers.GetVersion()) THIS DOESN'T WORK
            )
            .DataSource(BuildDataSource(featureFlagOptions.FlagValues))
            .Events(Components.NoEvents);

        _client?.Dispose();
        _client = new LdClient(builder.Build());
    }

    private TestData BuildDataSource(Dictionary<string, string> data)
    {
        _loggerFactory.CreateLogger("Test").LogWarning("KnownValues: {Count}", data.Count);
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
