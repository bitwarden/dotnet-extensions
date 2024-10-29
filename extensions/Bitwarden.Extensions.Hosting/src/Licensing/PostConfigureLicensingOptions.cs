using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Extensions.Hosting.Licensing;

internal sealed class PostConfigureLicensingOptions : IPostConfigureOptions<LicensingOptions>
{
    private readonly InternalLicensingOptions _internalLicensingOptions;
    private readonly ILogger<PostConfigureLicensingOptions> _logger;
    private readonly IHostEnvironment _hostEnvironment;

    public PostConfigureLicensingOptions(
        IOptions<InternalLicensingOptions> internalLicensingOptions,
        ILogger<PostConfigureLicensingOptions> logger,
        IHostEnvironment hostEnvironment)
    {
        _internalLicensingOptions = internalLicensingOptions.Value;
        _logger = logger;
        _hostEnvironment = hostEnvironment;
    }

    public void PostConfigure(string? name, LicensingOptions options)
    {
        if (name != Options.DefaultName)
        {
            return;
        }

        if (options.SigningCertificate != null)
        {
            // Something already set it, no problem, return early.
            return;
        }

        void DoFinalValidation()
        {
            var signingCertificate = options.SigningCertificate;

            if (signingCertificate == null)
            {
                throw new InvalidOperationException("No signing certificate could be retrieved.");
            }

            // TODO: Validate thumbprint one last time

            if (options.ForceSelfHost && !_hostEnvironment.IsDevelopment())
            {
                // Force self host is only allowed when running as development
                options.ForceSelfHost = false;
            }
        }


        // Try Azure Blob first
        if (TryAzureBlob(options))
        {
            DoFinalValidation();
            return;
        }


        // Try Cert store
        if (TryCertStore(options))
        {
            DoFinalValidation();
            return;
        }


        // Try Assembly embedded
        if (TryGetEmbeddedCert(options))
        {
            DoFinalValidation();
            return;
        }

        // TODO: Throw good exception
        throw new InvalidOperationException("Signing certificate could not be attained.");
    }

    private bool TryAzureBlob(LicensingOptions options)
    {
        if (string.IsNullOrEmpty(options.AzureBlob.ConnectionString))
        {
            return false;
        }

        // Infer them as trying to do azure blob
        if (string.IsNullOrEmpty(options.AzureBlob.CertificatePassword))
        {
            // TODO: Use logger generator
            _logger.LogWarning("An Azure Blob connection string but not a certificate password -- did you miss something?");
            return false;
        }

        // TODO: Do azure blob stuff
        // TODO: Set SigningCertificate
        return true;
    }

    private bool TryCertStore(LicensingOptions options)
    {
        var thumbprint = _hostEnvironment.IsDevelopment()
            ? _internalLicensingOptions.DevelopmentThumbprint
            : _internalLicensingOptions.NonDevelopmentThumbprint;

        using var certStore = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        certStore.Open(OpenFlags.ReadOnly);
        var certCollection = certStore.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
        if (certCollection.Count == 0)
        {
            return false;
        }

        options.SigningCertificate = certCollection[0];
        return true;
    }

    private bool TryGetEmbeddedCert(LicensingOptions options)
    {
        try
        {
            var appAssembly = Assembly.Load(new AssemblyName(_hostEnvironment.ApplicationName));
            // TODO: Make cert name configurable through internal options?
            var certName = _hostEnvironment.IsDevelopment()
                ? "licensing_dev.cer"
                : "licensing.cer";

            var resourceName = appAssembly.GetManifestResourceNames().Single(n => n.EndsWith(certName));

            using var resourceStream = appAssembly.GetManifestResourceStream(resourceName)!;
            using var memoryStream = new MemoryStream();
            resourceStream.CopyTo(memoryStream);
            options.SigningCertificate = new X509Certificate2(memoryStream.ToArray());
            return true;
        }
        catch (FileNotFoundException)
        {
            // TODO: Log warning
            return false;
        }
    }
}
