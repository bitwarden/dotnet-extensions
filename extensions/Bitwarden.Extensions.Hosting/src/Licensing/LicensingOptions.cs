using System.Security.Cryptography.X509Certificates;

namespace Bitwarden.Extensions.Hosting.Licensing;

/// <summary>
/// A set of options for customizing how licensing behaves.
/// </summary>
public sealed class LicensingOptions
{
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
/// It's important that these can't be set through configuration, only through code.
/// </summary>
/// <remarks>
/// We would need to make this public for services to customize this.
/// </remarks>
internal sealed class InternalLicensingOptions
{
    public string DevelopmentThumbprint { get; set; } = "207E64A231E8AA32AAF68A61037C075EBEBD553F";
    public string NonDevelopmentThumbprint { get; set; } = "B34876439FCDA2846505B2EFBBA6C4A951313EBE";
}
