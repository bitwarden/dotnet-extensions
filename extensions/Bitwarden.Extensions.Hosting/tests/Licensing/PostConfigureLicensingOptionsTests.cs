using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Azure.Storage.Blobs;
using Bitwarden.Extensions.Hosting.Licensing;
using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit.Abstractions;

namespace Bitwarden.Extensions.Hosting.Tests.Licensing;

public class PostConfigureLicensingOptionsTests
{
    private readonly InternalLicensingOptions _internalLicensingOptions;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILoggerFactory _loggerFactory;

    private readonly PostConfigureLicensingOptions _sut;

    public PostConfigureLicensingOptionsTests(ITestOutputHelper testOutputHelper)
    {
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddXunit(testOutputHelper);
        });

        _internalLicensingOptions = new InternalLicensingOptions();
        _hostEnvironment = Substitute.For<IHostEnvironment>();

        _sut = new PostConfigureLicensingOptions(
            Options.Create(_internalLicensingOptions),
            _loggerFactory.CreateLogger<PostConfigureLicensingOptions>(),
            _hostEnvironment
        );
    }

    [Fact]
    public void PostConfigure_NoBlobConfigured_NotInStore_LoadsProductionCert()
    {
        var allowedCertThumbprint = "569A8AD2907FB3A20DE200C04C8B1069E90F20AD";

        // Update our test cert as the allowed certificate
        _internalLicensingOptions.NonDevelopmentThumbprint = allowedCertThumbprint;

        _hostEnvironment
            .ApplicationName
            .Returns("Bitwarden.Extensions.Hosting.Tests");

        _hostEnvironment
            .EnvironmentName
            .Returns("Production");

        var options = new LicensingOptions();

        _sut.PostConfigure(Options.DefaultName, options);

        Assert.NotNull(options.SigningCertificate);
        Assert.Equal(allowedCertThumbprint, options.SigningCertificate.Thumbprint);
    }

    [Fact]
    public void PostConfigure_NoBlobConfigured_NotInStore_LoadsDevCert()
    {
        var allowedCertThumbprint = "AC6C1CDD9050FC943A4A67DAA181C85CF89AE9C7";

        // Update our test cert as the allowed certificate
        _internalLicensingOptions.DevelopmentThumbprint = allowedCertThumbprint;

        _hostEnvironment
            .ApplicationName
            .Returns("Bitwarden.Extensions.Hosting.Tests");

        _hostEnvironment
            .EnvironmentName
            .Returns("Development");

        var options = new LicensingOptions();

        _sut.PostConfigure(Options.DefaultName, options);

        Assert.NotNull(options.SigningCertificate);
        Assert.Equal(allowedCertThumbprint, options.SigningCertificate.Thumbprint);
    }

    [Fact]
    public void PostConfigure_NoBlobConfigured_InStore_Development_LoadsStoreCert()
    {
        var allowedCertThumbprint = "AC6C1CDD9050FC943A4A67DAA181C85CF89AE9C7";

        // Update our test cert as the allowed certificate
        _internalLicensingOptions.DevelopmentThumbprint = allowedCertThumbprint;

        _hostEnvironment
            .EnvironmentName
            .Returns("Development");

        var options = new LicensingOptions();

        UseTempStoreCert(
            "Bitwarden.Extensions.Hosting.Tests.Resources.licensing_dev.cer",
            allowedCertThumbprint, () =>
            {
                _sut.PostConfigure(Options.DefaultName, options);
            });

        Assert.NotNull(options.SigningCertificate);
        Assert.Equal(allowedCertThumbprint, options.SigningCertificate.Thumbprint);
    }

    [Fact]
    public void PostConfigure_NoBlobConfigured_InStore_Production_LoadsStoreCert()
    {
        var allowedCertThumbprint = "569A8AD2907FB3A20DE200C04C8B1069E90F20AD";

        // Update our test cert as the allowed certificate
        _internalLicensingOptions.NonDevelopmentThumbprint = allowedCertThumbprint;

        _hostEnvironment
            .EnvironmentName
            .Returns("Production");

        var options = new LicensingOptions();

        UseTempStoreCert(
            "Bitwarden.Extensions.Hosting.Tests.Resources.licensing.cer",
            allowedCertThumbprint, () =>
            {
                _sut.PostConfigure(Options.DefaultName, options);
            });

        Assert.NotNull(options.SigningCertificate);
        Assert.Equal(allowedCertThumbprint, options.SigningCertificate.Thumbprint);
    }

    [Fact]
    public async Task PostConfigure_InBlob_RetrievesCertFromBlob()
    {
        await using var test = await PrepareBlobStorageAsync();

        var allowedCertThumbprint = "AC6C1CDD9050FC943A4A67DAA181C85CF89AE9C7";

        _hostEnvironment
            .EnvironmentName
            .Returns("Development");

        _internalLicensingOptions.DevelopmentThumbprint = allowedCertThumbprint;

        var options = new LicensingOptions();
        options.AzureBlob.ConnectionString = "UseDevelopmentStorage=true;";
        options.AzureBlob.CertificatePassword = TestData.PfxPassword;

        _sut.PostConfigure(Options.DefaultName, options);

        Assert.NotNull(options.SigningCertificate);
        Assert.Equal(allowedCertThumbprint, options.SigningCertificate.Thumbprint);
    }

    [Fact]
    public async Task PostConfigure_InBlob_CustomOptions_RetrievesCertFromBlob()
    {
        await using var test = await PrepareBlobStorageAsync("custom", "myLicense.pfx");

        var allowedCertThumbprint = "AC6C1CDD9050FC943A4A67DAA181C85CF89AE9C7";

        _hostEnvironment
            .EnvironmentName
            .Returns("Development");

        _internalLicensingOptions.DevelopmentThumbprint = allowedCertThumbprint;

        var options = new LicensingOptions();
        options.AzureBlob.ConnectionString = "UseDevelopmentStorage=true;";
        options.AzureBlob.CertificatePassword = TestData.PfxPassword;
        options.AzureBlob.BlobName = "custom";
        options.AzureBlob.LicenseName = "myLicense.pfx";

        _sut.PostConfigure(Options.DefaultName, options);

        Assert.NotNull(options.SigningCertificate);
        Assert.Equal(allowedCertThumbprint, options.SigningCertificate.Thumbprint);
    }

    private async Task<IAsyncDisposable> PrepareBlobStorageAsync(
        string containerName = "certificates",
        string licenseName = "licensing.pfx")
    {
        var container = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/azure-storage/azurite:3.33.0")
            .WithPortBinding(10000, 10000) // Default port for blob storage
            .WithLogger(_loggerFactory.CreateLogger("Testcontainer"))
            .Build();

        await container.StartAsync();

        // Add certs to blob storage
        var blobServiceClient = new BlobServiceClient("UseDevelopmentStorage=true;");
        var blobContainerClient = blobServiceClient.CreateBlobContainer(containerName).Value;
        var blobClient = blobContainerClient.GetBlobClient(licenseName);

        await blobClient.UploadAsync(new BinaryData(TestData.TestCertificateWithPrivateKey));

        return container;
    }

    private void UseTempStoreCert(string resourceName, string thumbprint, Action test)
    {
        X509Certificate2? certificate = null;
        try
        {
            var certStore = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            certStore.Open(OpenFlags.ReadWrite);

            using var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)!;
            using var memoryStream = new MemoryStream();
            resourceStream.CopyTo(memoryStream);
            certificate = new X509Certificate2(memoryStream.ToArray());

            // Test code should never have us place a cert that different from the given thumbprint.
            Debug.Assert(certificate.Thumbprint == thumbprint);

            certStore.Add(certificate);

            // Close the store before running the test
            certStore.Dispose();

            test();
        }
        finally
        {
            // Was the certificate loaded?
            if (certificate != null)
            {
                // Delete from store via thumbprint
                using var deletingCertStore = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                deletingCertStore.Open(OpenFlags.ReadWrite);
                deletingCertStore.Remove(certificate);
            }
        }
    }
}
