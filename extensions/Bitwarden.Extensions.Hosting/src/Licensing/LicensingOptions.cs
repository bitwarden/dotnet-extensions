using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Extensions.Hosting.Licensing;

/// <summary>
/// A set of options for customizing how licensing behaves.
/// </summary>
public sealed class LicensingOptions
{
    /// <summary>
    /// The base url of the cloud instance.
    /// </summary>
    public string CloudHost { get; set; } = "bitwarden.com";

    /// <summary>
    /// Options for configuring license retrieval from azure blob storage.
    /// </summary>
    public AzureBlobLicensingOptions AzureBlob { get; set; } = new AzureBlobLicensingOptions();

    /// <summary>
    /// The certificate that will be used to either sign or validate licenses.
    /// </summary>
    public X509Certificate2 SigningCertificate { get; set; } = null!;

    /// <summary>
    /// Development option to force the usage of self hosted organization even though a certificate
    /// that can sign licenses it available.
    /// </summary>
    public bool ForceSelfHost { get; set; }
}

/// <summary>
/// A set of options for customizing how to find a certificate in Azure blob storage.
/// </summary>
public sealed class AzureBlobLicensingOptions
{
    /// <summary>
    /// The connection string to the azure blob storage account.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The password for the certificate stored in azure blob storage.
    /// </summary>
    public string? CertificatePassword { get; set; }

    // TODO: Do we actually need to allow these to be customized?
    /// <summary>
    /// The name of the blob the certificate is stored in, defaults to <c>certificates</c>.
    /// </summary>
    public string BlobName { get; set; } = "certificates";

    /// <summary>
    /// The name of the license stored in azure blob storage, defaults to <c>licensing.pfx</c>.
    /// </summary>
    public string LicenseName { get; set; } = "licensing.pfx";
}

/// <summary>
///
/// </summary>
/// <remarks>
/// You should not allow these to be set through configuration, only through code.
/// </remarks>
public sealed class InternalLicensingOptions
{
    /// <summary>
    /// The name of the product, defaults to the <see cref="IHostEnvironment.ApplicationName"/>.
    /// </summary>
    /// <remarks>
    /// This should NOT be changed once you have issued licenses, changing this will invalidate any already existing licenses.
    /// </remarks>
    public string ProductName { get; set; } = null!;

    /// <summary>
    /// The thumbprint of the certificate that should be allowed when running in development mode.
    /// </summary>
    public string DevelopmentThumbprint { get; set; } = "207E64A231E8AA32AAF68A61037C075EBEBD553F";

    /// <summary>
    /// The thumbprint of the certificate that should be allowed when running in non-development mode.
    /// </summary>
    public string NonDevelopmentThumbprint { get; set; } = "B34876439FCDA2846505B2EFBBA6C4A951313EBE";
}
