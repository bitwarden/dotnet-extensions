using System.Diagnostics;
using Bitwarden.Server.Sdk.Features;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Server;
using LaunchDarkly.Sdk.Server.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Bitwarden.Server.Sdk.UnitTests.Features;

public class LaunchDarklyFeatureServiceTests
{
    private class FakeLaunchDarklyClientProvider(ILdClient client) : ILaunchDarklyClientProvider
    {
        public ILdClient Get()
        {
            return client;
        }
    }

    private readonly ILdClient _ldClient;
    private readonly IContextBuilder _contextBuilder;
    private readonly IOptionsMonitor<FeatureFlagOptions> _featureFlagOptions;
    private readonly IHttpContextAccessor _httpContextAccessor;

    private readonly LaunchDarklyFeatureService _sut;

    public LaunchDarklyFeatureServiceTests()
    {
        _ldClient = Substitute.For<ILdClient>();
        _contextBuilder = Substitute.For<IContextBuilder>();
        _featureFlagOptions = Substitute.For<IOptionsMonitor<FeatureFlagOptions>>();
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();

        _sut = new LaunchDarklyFeatureService(
            new FakeLaunchDarklyClientProvider(_ldClient),
            _contextBuilder,
            _featureFlagOptions,
            NullLogger<LaunchDarklyFeatureService>.Instance,
            _httpContextAccessor
        );
    }

    [Fact]
    public void GetAll()
    {
        var flagsState = FeatureFlagsState.Builder()
            .AddFlag("feature-one", new EvaluationDetail<LdValue>(LdValue.Of(true), 0, default))
            .AddFlag("feature-two", new EvaluationDetail<LdValue>(LdValue.Of(1), 1, default))
            .AddFlag("feature-three", new EvaluationDetail<LdValue>(LdValue.Of("test-value"), 2, default))
            .Build();

        _ldClient.AllFlagsState(Arg.Any<Context>())
            .Returns(flagsState);

        _featureFlagOptions.CurrentValue.Returns(new FeatureFlagOptions
        {
            KnownFlags = ["feature-one", "feature-two", "feature-three"],
        });

        var allFlags = _sut.GetAll();

        Assert.Equal(3, allFlags.Count);

        var featureOneValue = Assert.Contains("feature-one", allFlags);
        Assert.True(featureOneValue.GetValue<bool>());

        var featureTwoValue = Assert.Contains("feature-two", allFlags);
        Assert.Equal(1, featureTwoValue.GetValue<int>());

        var featureThreeValue = Assert.Contains("feature-three", allFlags);
        Assert.Equal("test-value", featureThreeValue.GetValue<string>());
    }

    [Fact]
    public void GetAll_OnlyReturnsKnownFlags()
    {
        var flagsState = FeatureFlagsState.Builder()
            .AddFlag("feature-one", new EvaluationDetail<LdValue>(LdValue.Of(true), 0, default))
            .AddFlag("feature-two", new EvaluationDetail<LdValue>(LdValue.Of(true), 1, default))
            .Build();

        _ldClient.AllFlagsState(Arg.Any<Context>())
            .Returns(flagsState);

        _featureFlagOptions.CurrentValue.Returns(new FeatureFlagOptions
        {
            KnownFlags = ["feature-one"],
        });

        var allFlags = _sut.GetAll();

        Assert.Single(allFlags);
        var flagValue = Assert.Contains("feature-one", allFlags);
        Assert.True(flagValue.GetValue<bool>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsEnabled_PassesAlongDetails(bool defaultValue)
    {
        _ldClient
            .BoolVariation("feature-one", Arg.Any<Context>(), defaultValue)
            .Returns(true);

        Assert.True(_sut.IsEnabled("feature-one", defaultValue));
    }

    [Fact]
    public void IsEnabled_MultipleCalls_BuildsContextOnce()
    {
        _ = _sut.IsEnabled("feature-one");
        _ = _sut.IsEnabled("feature-one");

        // Use the access of the HttpContext as the indicator that it was only built from once
        _contextBuilder.Received(1).Build();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public void GetIntVariation_PassesAlongDetails(int defaultValue)
    {
        _ldClient
            .IntVariation("feature-one", Arg.Any<Context>(), defaultValue)
            .Returns(1);

        Assert.Equal(1, _sut.GetIntVariation("feature-one", defaultValue));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("test")]
    public void GetStringVariation_PassesAlongDetails(string? defaultValue)
    {
        _ldClient
            .StringVariation("feature-one", Arg.Any<Context>(), defaultValue)
            .Returns("my-value");

        Assert.Equal("my-value", _sut.GetStringVariation("feature-one", defaultValue));
    }

    [Fact]
    public void IsEnabled_AddsTagToHttpActivity()
    {
        var activity = new Activity("test-activity");

        var httpContext = Substitute.For<HttpContext>();
        var activityFeature = Substitute.For<IHttpActivityFeature>();
        activityFeature.Activity.Returns(activity);
        httpContext.Features.Get<IHttpActivityFeature>().Returns(activityFeature);

        var metricsTagsFeature = Substitute.For<IHttpMetricsTagsFeature>();
        var metricsTags = new List<KeyValuePair<string, object?>>();
        metricsTagsFeature.Tags.Returns(metricsTags);
        httpContext.Features.Get<IHttpMetricsTagsFeature>().Returns(metricsTagsFeature);

        _httpContextAccessor.HttpContext.Returns(httpContext);

        _ldClient
            .BoolVariation("feature-one", Arg.Any<Context>(), false)
            .Returns(true);

        _sut.IsEnabled("feature-one");

        var activityTag = Assert.Single(activity.TagObjects, t => t.Key == "feature.flag.feature-one");
        Assert.Equal(true, activityTag.Value);

        var metricsTag = Assert.Single(metricsTags, t => t.Key == "feature.flag.feature-one");
        Assert.Equal(true, metricsTag.Value);
    }

    [Fact]
    public void GetIntVariation_AddsTagToHttpActivity()
    {
        var activity = new Activity("test-activity");

        var httpContext = Substitute.For<HttpContext>();
        var activityFeature = Substitute.For<IHttpActivityFeature>();
        activityFeature.Activity.Returns(activity);
        httpContext.Features.Get<IHttpActivityFeature>().Returns(activityFeature);

        var metricsTagsFeature = Substitute.For<IHttpMetricsTagsFeature>();
        var metricsTags = new List<KeyValuePair<string, object?>>();
        metricsTagsFeature.Tags.Returns(metricsTags);
        httpContext.Features.Get<IHttpMetricsTagsFeature>().Returns(metricsTagsFeature);

        _httpContextAccessor.HttpContext.Returns(httpContext);

        _ldClient
            .IntVariation("feature-two", Arg.Any<Context>(), 0)
            .Returns(42);

        _sut.GetIntVariation("feature-two");

        var activityTag = Assert.Single(activity.TagObjects, t => t.Key == "feature.flag.feature-two");
        Assert.Equal(42, activityTag.Value);

        var metricsTag = Assert.Single(metricsTags, t => t.Key == "feature.flag.feature-two");
        Assert.Equal(42, metricsTag.Value);
    }

    [Fact]
    public void GetStringVariation_AddsTagToHttpActivity()
    {
        var activity = new Activity("test-activity");

        var httpContext = Substitute.For<HttpContext>();
        var activityFeature = Substitute.For<IHttpActivityFeature>();
        activityFeature.Activity.Returns(activity);
        httpContext.Features.Get<IHttpActivityFeature>().Returns(activityFeature);

        var metricsTagsFeature = Substitute.For<IHttpMetricsTagsFeature>();
        var metricsTags = new List<KeyValuePair<string, object?>>();
        metricsTagsFeature.Tags.Returns(metricsTags);
        httpContext.Features.Get<IHttpMetricsTagsFeature>().Returns(metricsTagsFeature);

        _httpContextAccessor.HttpContext.Returns(httpContext);

        _ldClient
            .StringVariation("feature-three", Arg.Any<Context>(), null)
            .Returns("test-value");

        _sut.GetStringVariation("feature-three");

        var activityTag = Assert.Single(activity.TagObjects, t => t.Key == "feature.flag.feature-three");
        Assert.Equal("test-value", activityTag.Value);

        var metricsTag = Assert.Single(metricsTags, t => t.Key == "feature.flag.feature-three");
        Assert.Equal("test-value", metricsTag.Value);
    }

    [Fact]
    public void IsEnabled_NoHttpContext_DoesNotThrow()
    {
        _httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        _ldClient
            .BoolVariation("feature-one", Arg.Any<Context>(), false)
            .Returns(true);

        var result = _sut.IsEnabled("feature-one");

        Assert.True(result);
    }

    [Fact]
    public void IsEnabled_MultipleCalls_AddsMultipleTags()
    {
        var activity = new Activity("test-activity");

        var httpContext = Substitute.For<HttpContext>();
        var activityFeature = Substitute.For<IHttpActivityFeature>();
        activityFeature.Activity.Returns(activity);
        httpContext.Features.Get<IHttpActivityFeature>().Returns(activityFeature);
        _httpContextAccessor.HttpContext.Returns(httpContext);

        _ldClient
            .BoolVariation("feature-one", Arg.Any<Context>(), false)
            .Returns(true, false);

        // First call should add the tag with value true
        _sut.IsEnabled("feature-one");

        // Second call should add another tag with value false
        _sut.IsEnabled("feature-one");

        var tags = activity.TagObjects.Where(t => t.Key == "feature.flag.feature-one").ToList();
        Assert.Equal(2, tags.Count);
        Assert.Equal(true, tags[0].Value);
        Assert.Equal(false, tags[1].Value);
    }
}
