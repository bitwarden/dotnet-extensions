using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Common.Tests;

// An extension is declared on its assembly, on purpose, and may be declared more than once.
public sealed class IServiceExtensionTests
{
    [Fact]
    public void Attribute_KeepsTheExtensionType()
    {
        Assert.Equal(typeof(Marker), new ServiceExtensionAttribute(typeof(Marker)).Extension);
    }

    [Fact]
    public void Attribute_IsDeclaredOnAssemblies_AndMayRepeat()
    {
        var usage = typeof(ServiceExtensionAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Assembly, usage.ValidOn);
        Assert.True(usage.AllowMultiple);
    }

    [Fact]
    public void Configure_AddsTheExtensionsRegistrations()
    {
        var services = new ServiceCollection();

        IServiceExtension extension = new Marker();
        extension.Configure(services, new NoConfiguration());

        Assert.Contains(services, d => d.ServiceType == typeof(Marker));
    }

    private sealed class Marker : IServiceExtension
    {
        public void Configure(IServiceCollection services, IConfiguration configuration) => services.AddSingleton<Marker>();
    }

    private sealed class NoConfiguration : IConfiguration
    {
        public string? this[string key] { get => null; set { } }

        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();
    }
}
