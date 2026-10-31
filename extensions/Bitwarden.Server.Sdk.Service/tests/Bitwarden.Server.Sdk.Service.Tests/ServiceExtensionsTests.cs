using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: ServiceExtension(typeof(ServiceExtensionsTests.Marker))]

namespace Bitwarden.Server.Sdk.Service.Tests;

public sealed class ServiceExtensionsTests
{
    [Fact]
    public void Declared_FindsAnExtensionOnTheServiceAssembly()
    {
        var actual = ServiceExtensions.Declared(typeof(ServiceExtensionsTests).Assembly);

        Assert.Contains(typeof(Marker), actual);
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
    public sealed class Marker : IServiceExtension
    {
        /// <inheritdoc />
        public void Configure(IServiceCollection services, IConfiguration configuration) =>
            services.AddSingleton<Marker>();
    }
}
