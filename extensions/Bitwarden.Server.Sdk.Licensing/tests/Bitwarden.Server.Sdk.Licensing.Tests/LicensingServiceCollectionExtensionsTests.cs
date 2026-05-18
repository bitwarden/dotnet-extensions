using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography.X509Certificates;
using Azure.Storage.Blobs;
using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Bitwarden.Server.Sdk.Licensing.Tests;

public record User(string Name);

public class LicensingServiceCollectionExtensionsTests
{
    // Created using: var now = DateTimeOffset.UtcNow;
    // using var rsa = RSA.Create(2048);
    // var certificate = new CertificateRequest("CN=example.com", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
    //    .CreateSelfSigned(now, now.AddDays(30));
    // Convert.ToBase64String(certificate.Export(X509ContentType.Pfx));
    private static byte[] CertificateWithPrivateKey => Convert.FromBase64String(@"
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
tmYQSzIhrp9BchyTe/R+AgIH0A==");

    [Fact]
    public async Task CertificateRoundTrips()
    {
        var services = new ServiceCollection();

        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.EnvironmentName = "Development";
        services.AddSingleton(hostEnvironment);

        var certificate = new X509Certificate2(CertificateWithPrivateKey);

        services.AddLicensing(new StaticLicensingOptions
        {
            Issuer = "test_iss",
            Audience = "test_aud",
            EnvironmentThumbprints = new Dictionary<string, string>(),
            FallbackThumbprint = certificate.Thumbprint,
        });

        services.AddLicenseFactory<User, TestLicenseFactory>();

        var signingCertificateProvider = Substitute.For<ISigningCertificateProvider>();
        signingCertificateProvider
            .Get()
            .Returns(certificate);

        services.AddSingleton(signingCertificateProvider);

        var issuerCertificateProvider = Substitute.For<IIssuerCertificateProvider>();
        issuerCertificateProvider
            .Get()
            .Returns(certificate);

        services.AddSingleton(issuerCertificateProvider);

        var provider = services.BuildServiceProvider();

        var licenseGenerator = provider.GetRequiredService<ILicenseGenerator<User>>();

        var user = new User("John");

        var license = await licenseGenerator.GenerateAsync(user, DateTimeOffset.UtcNow.AddDays(10), TestContext.Current.CancellationToken);

        var licenseReader = provider.GetRequiredService<ILicenseReader<User>>();

        var claims = await licenseReader.ReadLicenseAsync(license);

        var value = Assert.Contains("user_name", claims);
        Assert.Equal("John", value);
    }

    [Fact]
    public async Task RetrievesCertificateFromAzureBlobStorage()
    {
        await using var azurite = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/azure-storage/azurite")
            .WithPortBinding(10000) // Blob port
            .Build();

        await azurite.StartAsync(TestContext.Current.CancellationToken);
        var azuriteConnectionString = $"UseDevelopmentStorage=true;DevelopmentStorageProxyUri=http://{azurite.Hostname}:{azurite.GetMappedPublicPort(10000)}";

        var client = new BlobServiceClient(azuriteConnectionString);
        var container = await client.CreateBlobContainerAsync("certificates", cancellationToken: TestContext.Current.CancellationToken);
        await container.Value.UploadBlobAsync("licensing.pfx", new BinaryData(CertificateWithPrivateKey), TestContext.Current.CancellationToken);

        await CreateHostAsync(
            hostBuilder =>
            {
                hostBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "Licensing:Azure:ConnectionString", azuriteConnectionString },
                });

                using var certificate = new X509Certificate2(CertificateWithPrivateKey);

                hostBuilder.Services.AddLicensing(new StaticLicensingOptions
                {
                    Issuer = "blob_iss",
                    Audience = "blob_aud",
                    EnvironmentThumbprints = new Dictionary<string, string>(),
                    FallbackThumbprint = certificate.Thumbprint,
                });

                hostBuilder.Services.AddLicenseFactory<User, TestLicenseFactory>();
            },
            async host =>
            {
                var licenseGenerator = host.Services.GetRequiredService<ILicenseGenerator<User>>();

                var license = await licenseGenerator.GenerateAsync(
                    new User("John"),
                    DateTimeOffset.UtcNow.AddDays(2),
                    TestContext.Current.CancellationToken
                );

                var token = new JwtSecurityTokenHandler().ReadJwtToken(license);

                Assert.Equal("blob_aud", Assert.Single(token.Audiences));
                Assert.Equal("blob_iss", token.Issuer);
            }
        );
    }

    [Fact]
    public void IssuerCertificateProviderSelectsEnvironmentSpecificThumbprint()
    {
        const string stagingThumbprint = "105E982660B664EFF370CD23793023BDC8D8C939";

        var services = new ServiceCollection();

        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.ApplicationName = typeof(LicensingServiceCollectionExtensionsTests).Assembly.GetName().Name!;
        hostEnvironment.EnvironmentName = "Staging";
        services.AddSingleton(hostEnvironment);

        using var fallback = new X509Certificate2(CertificateWithPrivateKey);

        services.AddLicensing(new StaticLicensingOptions
        {
            Issuer = "iss",
            Audience = "aud",
            EnvironmentThumbprints = new Dictionary<string, string>
            {
                ["Staging"] = stagingThumbprint,
            },
            FallbackThumbprint = fallback.Thumbprint,
        });

        var provider = services.BuildServiceProvider();
        var issuerCertificateProvider = provider.GetRequiredService<IIssuerCertificateProvider>();

        var certificate = issuerCertificateProvider.Get();

        Assert.Equal(stagingThumbprint, certificate.Thumbprint);
        Assert.NotEqual(fallback.Thumbprint, certificate.Thumbprint);
    }

    [Fact]
    public void IssuerCertificateProviderLoadsCertificateFromEmbeddedResource()
    {
        var services = new ServiceCollection();

        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.ApplicationName = typeof(LicensingServiceCollectionExtensionsTests).Assembly.GetName().Name!;
        hostEnvironment.EnvironmentName = "Production";
        services.AddSingleton(hostEnvironment);

        using var expected = new X509Certificate2(CertificateWithPrivateKey);

        services.AddLicensing(new StaticLicensingOptions
        {
            Issuer = "iss",
            Audience = "aud",
            EnvironmentThumbprints = new Dictionary<string, string>(),
            FallbackThumbprint = expected.Thumbprint,
        });

        var provider = services.BuildServiceProvider();
        var issuerCertificateProvider = provider.GetRequiredService<IIssuerCertificateProvider>();

        var certificate = issuerCertificateProvider.Get();

        Assert.Equal(expected.Thumbprint, certificate.Thumbprint);
        Assert.False(certificate.HasPrivateKey);
    }

    [Fact]
    public void IssuerCertificateProviderThrowsWhenEmbeddedResourceIsMissing()
    {
        var services = new ServiceCollection();

        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.ApplicationName = typeof(LicensingServiceCollectionExtensionsTests).Assembly.GetName().Name!;
        hostEnvironment.EnvironmentName = "Production";
        services.AddSingleton(hostEnvironment);

        var missingThumbprint = new string('0', 40);

        services.AddLicensing(new StaticLicensingOptions
        {
            Issuer = "iss",
            Audience = "aud",
            EnvironmentThumbprints = new Dictionary<string, string>(),
            FallbackThumbprint = missingThumbprint,
        });

        var provider = services.BuildServiceProvider();
        var issuerCertificateProvider = provider.GetRequiredService<IIssuerCertificateProvider>();

        var ex = Assert.Throws<InvalidOperationException>(() => issuerCertificateProvider.Get());

        Assert.Contains($"Licensing.{missingThumbprint}.cer", ex.Message);
    }

    [Fact]
    public void LicenseGeneratorThrowsWhenNoClaimsFactoryIsRegistered()
    {
        var services = new ServiceCollection();

        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.EnvironmentName = "Development";
        services.AddSingleton(hostEnvironment);

        var certificate = new X509Certificate2(CertificateWithPrivateKey);

        services.AddLicensing(new StaticLicensingOptions
        {
            Issuer = "iss",
            Audience = "aud",
            EnvironmentThumbprints = new Dictionary<string, string>(),
            FallbackThumbprint = certificate.Thumbprint,
        });

        var signingCertificateProvider = Substitute.For<ISigningCertificateProvider>();
        signingCertificateProvider.Get().Returns(certificate);
        services.AddSingleton(signingCertificateProvider);

        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<ILicenseGenerator<User>>());

        Assert.Contains("ILicenseClaimsFactory", ex.Message);
    }

    [Fact]
    public void LicenseGeneratorThrowsWhenSigningCertificateLacksPrivateKey()
    {
        var services = new ServiceCollection();

        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.EnvironmentName = "Development";
        services.AddSingleton(hostEnvironment);

        using var withKey = new X509Certificate2(CertificateWithPrivateKey);
        var publicOnly = new X509Certificate2(withKey.Export(X509ContentType.Cert));

        services.AddLicensing(new StaticLicensingOptions
        {
            Issuer = "iss",
            Audience = "aud",
            EnvironmentThumbprints = new Dictionary<string, string>(),
            FallbackThumbprint = publicOnly.Thumbprint,
        });

        services.AddLicenseFactory<User, TestLicenseFactory>();

        var signingCertificateProvider = Substitute.For<ISigningCertificateProvider>();
        signingCertificateProvider.Get().Returns(publicOnly);
        services.AddSingleton(signingCertificateProvider);

        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<ILicenseGenerator<User>>());

        Assert.Contains("lacks a private key", ex.Message);
    }

    [Fact]
    public void LicenseGeneratorThrowsWhenSigningIsNotSupported()
    {
        var services = new ServiceCollection();

        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.EnvironmentName = "Production";
        services.AddSingleton(hostEnvironment);

        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddLicensing(new StaticLicensingOptions
        {
            Issuer = "iss",
            Audience = "aud",
            EnvironmentThumbprints = new Dictionary<string, string>(),
            FallbackThumbprint = new string('0', 40),
        });

        services.AddLicenseFactory<User, TestLicenseFactory>();

        var provider = services.BuildServiceProvider();

        Assert.Throws<NotSupportedException>(
            () => provider.GetRequiredService<ILicenseGenerator<User>>());
    }

    [Fact]
    public void LicenseGeneratorThrowsWhenSigningCertificateThumbprintDoesNotMatch()
    {
        var services = new ServiceCollection();

        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.EnvironmentName = "Development";
        services.AddSingleton(hostEnvironment);

        var certificate = new X509Certificate2(CertificateWithPrivateKey);
        var unexpectedThumbprint = new string('A', 40);

        services.AddLicensing(new StaticLicensingOptions
        {
            Issuer = "iss",
            Audience = "aud",
            EnvironmentThumbprints = new Dictionary<string, string>(),
            FallbackThumbprint = unexpectedThumbprint,
        });

        services.AddLicenseFactory<User, TestLicenseFactory>();

        var signingCertificateProvider = Substitute.For<ISigningCertificateProvider>();
        signingCertificateProvider.Get().Returns(certificate);
        services.AddSingleton(signingCertificateProvider);

        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<ILicenseGenerator<User>>());

        Assert.Contains("does not match the expected thumbprint", ex.Message);
    }

    [Fact]
    public async Task SigningCertificateActivatorSkipsWhenSigningIsNotSupported()
    {
        var signingCertificateProvider = Substitute.For<ISigningCertificateProvider>();
        signingCertificateProvider.IsSupported.Returns(false);

        using var certificate = new X509Certificate2(CertificateWithPrivateKey);

        await CreateHostAsync(
            hostBuilder =>
            {
                hostBuilder.Services.AddLicensing(new StaticLicensingOptions
                {
                    Issuer = "iss",
                    Audience = "aud",
                    EnvironmentThumbprints = new Dictionary<string, string>(),
                    FallbackThumbprint = certificate.Thumbprint,
                });

                hostBuilder.Services.AddSingleton(signingCertificateProvider);
            },
            host =>
            {
                signingCertificateProvider.DidNotReceive().Get();
                return Task.CompletedTask;
            }
        );
    }

    [Fact]
    public async Task ThrowsWhenAzureBlobStorageIsUnavailable()
    {
        // Valid-format connection string pointing at a port nothing is listening on, so the
        // SigningCertificateActivator's eager fetch fails during host startup.
        var unreachableConnectionString =
            "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
            "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
            "BlobEndpoint=http://127.0.0.1:1/devstoreaccount1;";

        var hostBuilder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        // Make this test run faster
        hostBuilder.Services.AddOptions<BlobClientOptions>("Licensing")
            .Configure(options =>
            {
                options.Retry.MaxRetries = 1;
            });

        hostBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "Licensing:Azure:ConnectionString", unreachableConnectionString },
        });

        using var certificate = new X509Certificate2(CertificateWithPrivateKey);

        hostBuilder.Services.AddLicensing(new StaticLicensingOptions
        {
            Issuer = "blob_iss",
            Audience = "blob_aud",
            EnvironmentThumbprints = new Dictionary<string, string>(),
            FallbackThumbprint = certificate.Thumbprint,
        });

        hostBuilder.Services.AddLicenseFactory<User, TestLicenseFactory>();

        using var host = hostBuilder.Build();

        var aggregateException = await Assert.ThrowsAsync<AggregateException>(
            () => host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains(aggregateException.InnerExceptions, (ex) => ex is Azure.RequestFailedException);
    }

    [Fact]
    public async Task RetrievesCertificateFromCertificateStore()
    {
        using var certificate = new X509Certificate2(
            CertificateWithPrivateKey,
            (string?)null,
            X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);

        using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
        {
            store.Open(OpenFlags.ReadWrite);
            store.Add(certificate);
        }

        try
        {
            await CreateHostAsync(
                hostBuilder =>
                {
                    hostBuilder.Services.AddLicensing(new StaticLicensingOptions
                    {
                        Issuer = "store_iss",
                        Audience = "store_aud",
                        EnvironmentThumbprints = new Dictionary<string, string>(),
                        FallbackThumbprint = certificate.Thumbprint,
                    });

                    hostBuilder.Services.AddLicenseFactory<User, TestLicenseFactory>();
                },
                async host =>
                {
                    var licenseGenerator = host.Services.GetRequiredService<ILicenseGenerator<User>>();

                    var license = await licenseGenerator.GenerateAsync(
                        new User("John"),
                        DateTimeOffset.UtcNow.AddDays(2),
                        TestContext.Current.CancellationToken
                    );

                    var token = new JwtSecurityTokenHandler().ReadJwtToken(license);

                    Assert.Equal("store_aud", Assert.Single(token.Audiences));
                    Assert.Equal("store_iss", token.Issuer);
                },
                new HostApplicationBuilderSettings { EnvironmentName = Environments.Development }
            );
        }
        finally
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Remove(certificate);
        }
    }

    private static async Task CreateHostAsync(
        Action<HostApplicationBuilder> configureHostBuilder,
        Func<IHost, Task> testAction,
        HostApplicationBuilderSettings? settings = null)
    {
        var hostBuilder = Host.CreateEmptyApplicationBuilder(settings ?? new HostApplicationBuilderSettings());

        configureHostBuilder(hostBuilder);

        using var host = hostBuilder.Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        await testAction(host);

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    private class TestLicenseFactory : ILicenseClaimsFactory<User>
    {
        public ValueTask AddClaimsAsync(LicenseClaimsContext context, User item, CancellationToken cancellationToken)
        {
            context.AddClaim("user_name", item.Name);
            return ValueTask.CompletedTask;
        }
    }
}
