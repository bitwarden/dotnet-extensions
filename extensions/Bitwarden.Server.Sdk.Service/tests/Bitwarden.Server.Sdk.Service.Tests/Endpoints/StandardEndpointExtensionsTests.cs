using Bitwarden.Server.Sdk.Service.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// <c>WithStandards</c> puts the standard endpoint filter on every endpoint a builder maps and,
/// once each endpoint's own conventions have run, the standards' convention.
/// </summary>
public sealed class StandardEndpointExtensionsTests
{
    [Fact]
    public void WithStandards_Builder_TheSameBuilder()
    {
        var builder = new Recording();

        Assert.Same(builder, builder.WithStandards());
    }

    [Fact]
    public void WithStandards_Endpoint_GetsTheFilterAndTheConvention()
    {
        var builder = new Recording().WithStandards();
        var endpoint = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("api/v1/widgets"), 0)
        {
            ApplicationServices = new ServiceCollection().AddOptions().AddSingleton<StandardEndpointProblems>().BuildServiceProvider(),
        };
        endpoint.Metadata.Add(typeof(StandardEndpointExtensionsTests).GetMethod(nameof(Handler), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!);

        builder.Build(endpoint);

        Assert.Single(endpoint.FilterFactories);
        Assert.Contains(StandardEndpointMetadata.Instance, endpoint.Metadata);
    }

    [Fact]
    public void WithStandards_NoBuilder_Refused()
    {
        Assert.Throws<ArgumentNullException>(() => ((Recording)null!).WithStandards());
    }

    private static string Handler() => string.Empty;

    /// <summary>
    /// Records the conventions given to it, and applies them in the framework's order: the
    /// conventions, then the final ones.
    /// </summary>
    private sealed class Recording : IEndpointConventionBuilder
    {
        private readonly List<Action<EndpointBuilder>> _conventions = [];
        private readonly List<Action<EndpointBuilder>> _finally = [];

        public void Add(Action<EndpointBuilder> convention) => _conventions.Add(convention);

        public void Finally(Action<EndpointBuilder> finallyConvention) => _finally.Add(finallyConvention);

        public void Build(EndpointBuilder endpoint)
        {
            foreach (var convention in _conventions.Concat(_finally))
            {
                convention(endpoint);
            }
        }
    }
}
