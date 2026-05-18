namespace Bitwarden.Server.Sdk.Licensing;

/// <summary>
///
/// </summary>
public class LicensingOptions
{
    /// <summary>
    ///
    /// </summary>
    public string? CertificatePassword { get; set; }

    /// <summary>
    ///
    /// </summary>
    public AzureBlobLicensingOptions Azure { get; set; } = new AzureBlobLicensingOptions();
}

/// <summary>
///
/// </summary>
public class AzureBlobLicensingOptions
{
    /// <summary>
    ///
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    ///
    /// </summary>
    public string ContainerName { get; set; } = "certificates";

    /// <summary>
    ///
    /// </summary>
    public string FileName { get; set; } = "licensing.pfx";
}
