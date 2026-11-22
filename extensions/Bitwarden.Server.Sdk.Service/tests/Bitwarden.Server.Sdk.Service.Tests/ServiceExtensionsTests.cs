using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: ServiceExtension(typeof(ServiceExtensionsTests.Marker))]

// Declared after Marker and named before it, so the order they are applied in is the names' and not the declarations'.
[assembly: ServiceExtension(typeof(ServiceExtensionsTests.Applied))]

namespace Bitwarden.Server.Sdk.Service.Tests;

public sealed class ServiceExtensionsTests
{
    [Fact]
    public void Declared_FindsAnExtensionOnTheServiceAssembly()
    {
        var actual = ServiceExtensions.Declared(typeof(ServiceExtensionsTests).Assembly);

        Assert.Contains(typeof(Marker), actual);
    }

    // Applied in an order that doesn't depend on how they were declared or found: by the declaring assembly's name, then
    // the extension's.
    [Fact]
    public void Declared_IsOrderedByAssemblyThenTypeName()
    {
        var actual = ServiceExtensions.Declared(typeof(ServiceExtensionsTests).Assembly);

        var ordered = actual
            .OrderBy(extension => extension.Assembly.GetName().Name, StringComparer.Ordinal)
            .ThenBy(extension => extension.FullName, StringComparer.Ordinal);
        Assert.Equal(ordered, actual);
        Assert.Equal([typeof(Applied), typeof(Marker)], actual.Where(extension => extension.Assembly == typeof(Marker).Assembly));
    }

    [Fact]
    public void Apply_RunsTheExtensionAgainstTheServicesRegistrations()
    {
        var services = new ServiceCollection();

        ServiceExtensions.Apply(
            typeof(ServiceExtensionsTests).Assembly,
            services,
            new ConfigurationBuilder().Build());

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(Marker));
    }

    /// <summary>
    /// Registers itself, so a test can see it ran.
    /// </summary>
    public sealed class Applied : IServiceExtension
    {
        /// <inheritdoc />
        public void Configure(IServiceCollection services, IConfiguration configuration) =>
            services.AddSingleton<Applied>();
    }

    /// <summary>
    /// Registers itself, so a test can see it ran.
    /// </summary>
    public sealed class Marker : IServiceExtension
    {
        /// <inheritdoc />
        public void Configure(IServiceCollection services, IConfiguration configuration) =>
            services.AddSingleton<Marker>();
    }
}
