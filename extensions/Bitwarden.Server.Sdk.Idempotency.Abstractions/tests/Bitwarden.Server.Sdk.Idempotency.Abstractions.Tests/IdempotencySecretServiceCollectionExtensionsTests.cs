using Bitwarden.Server.Sdk.CryptoPrimitives;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.Idempotency.Abstractions.Tests;

public sealed class IdempotencySecretServiceCollectionExtensionsTests
{
    private static readonly CacheSecret Secret = new(Enumerable.Repeat((byte)1, CacheSecret.Length).ToArray());

    [Fact]
    public void AddIdempotencySecret_ReadsTheConfiguredSecret()
    {
        var secret = Resolve(new ServiceCollection().AddIdempotencySecret(), IdempotencySecretTests.Configuration(Secret.ToString()), "Production");

        Assert.Equal(Secret, secret.Value);
    }

    [Fact]
    public void AddIdempotencySecret_UnsetInDevelopment_IsTheDevelopmentSecret()
    {
        var secret = Resolve(new ServiceCollection().AddIdempotencySecret(), IdempotencySecretTests.Configuration(null), "Development");

        Assert.Equal(CacheSecret.Development, secret.Value);
    }

    // A container built without a host has no environment, and is treated as not Development; one without
    // configuration has no secret.
    [Fact]
    public void AddIdempotencySecret_WithNoEnvironmentOrConfiguration_RequiresASecret()
    {
        var services = new ServiceCollection().AddIdempotencySecret();

        Assert.Throws<InvalidOperationException>(() => Resolve(services, IdempotencySecretTests.Configuration(null), null));
        Assert.Throws<InvalidOperationException>(() => Resolve(services, null, "Production"));
        Assert.Equal(CacheSecret.Development, Resolve(services, null, "Development").Value);
    }

    // Registered before, a secret is kept: a test or a host may supply its own.
    [Fact]
    public void AddIdempotencySecret_KeepsASecretAlreadyRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new IdempotencySecret(Secret));
        services.AddIdempotencySecret();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IdempotencySecret));
        Assert.Equal(Secret, Assert.IsType<IdempotencySecret>(descriptor.ImplementationInstance).Value);
    }

    private static IdempotencySecret Resolve(IServiceCollection services, IConfiguration? configuration, string? environment)
    {
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IdempotencySecret));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);

        return (IdempotencySecret)descriptor.ImplementationFactory!(new Provider(configuration, environment));
    }

    private sealed class Provider(IConfiguration? configuration, string? environment) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IConfiguration) ? configuration
            : serviceType == typeof(IHostEnvironment) && environment is not null ? new Environment(environment)
            : null;
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "test";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
