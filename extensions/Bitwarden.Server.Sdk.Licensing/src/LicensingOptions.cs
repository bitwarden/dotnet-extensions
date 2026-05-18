namespace Bitwarden.Server.Sdk.Licensing;

/// <summary>
/// Runtime licensing configuration, bound from the <c>Licensing</c> section of <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>.
/// </summary>
public class LicensingOptions
{
    /// <summary>
    /// Password used to unlock the signing certificate's private key, regardless of where the
    /// certificate is loaded from (Azure Blob Storage or the developer's certificate store).
    /// Leave <see langword="null"/> when the key is not password-protected.
    /// </summary>
    public string? CertificatePassword { get; set; }

    /// <summary>
    /// Configuration controlling retrieval of the signing certificate from Azure Blob Storage.
    /// </summary>
    public AzureBlobLicensingOptions Azure { get; set; } = new AzureBlobLicensingOptions();
}

/// <summary>
/// Options for retrieving a signing certificate from Azure Blob Storage.
/// </summary>
public class AzureBlobLicensingOptions
{
    /// <summary>
    /// Connection string for the Azure Storage account that holds the certificate. When
    /// <see langword="null"/> or empty, Azure Blob Storage is not used and the licensing system
    /// falls back to local resolution strategies.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The blob container holding the certificate. Defaults to <c>certificates</c>.
    /// </summary>
    public string ContainerName { get; set; } = "certificates";

    /// <summary>
    /// The blob name of the certificate. Defaults to <c>licensing.pfx</c>.
    /// </summary>
    public string FileName { get; set; } = "licensing.pfx";
}
