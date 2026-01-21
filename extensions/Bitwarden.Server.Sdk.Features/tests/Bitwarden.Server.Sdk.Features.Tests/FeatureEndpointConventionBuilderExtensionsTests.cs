using Bitwarden.Server.Sdk.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Bitwarden.Server.Sdk.UnitTests.Features;

public class FeatureEndpointConventionBuilderExtensionsTests
{
    [Fact]
    public void RequireFeature_ChainedCall()
    {
        // Arrange
        var builder = new TestEndpointConventionBuilder();

        // Act
        var chainedBuilder = builder.RequireFeature("feature-key");

        // Assert
        Assert.True(chainedBuilder.TestProperty);
    }

    [Fact]
    public void RequireFeature_WithFeatureKey()
    {
        // Arrange
        var builder = new TestEndpointConventionBuilder();

        // Act
        builder.RequireFeature("feature-key");

        // Assert
        var convention = Assert.Single(builder.Conventions);

        var endpointModel = new RouteEndpointBuilder((c) => Task.CompletedTask, RoutePatternFactory.Parse("/"), 0);
        convention(endpointModel);

        Assert.IsAssignableFrom<IFeatureMetadata>(Assert.Single(endpointModel.Metadata));
    }

    [Fact]
    public void RequireFeature_WithCallback()
    {
        // Arrange
        var builder = new TestEndpointConventionBuilder();

        // Act
        builder.RequireFeature(featureService => featureService.IsEnabled("my-feature"));

        // Assert
        var convention = Assert.Single(builder.Conventions);

        var endpointModel = new RouteEndpointBuilder((c) => Task.CompletedTask, RoutePatternFactory.Parse("/"), 0);
        convention(endpointModel);

        Assert.IsAssignableFrom<IFeatureMetadata>(Assert.Single(endpointModel.Metadata));
    }

    private sealed class TestEndpointConventionBuilder : IEndpointConventionBuilder
    {
        public List<Action<EndpointBuilder>> Conventions { get; } = [];
        public bool TestProperty { get; } = true;

        public void Add(Action<EndpointBuilder> convention)
        {
            Conventions.Add(convention);
        }
    }
}
