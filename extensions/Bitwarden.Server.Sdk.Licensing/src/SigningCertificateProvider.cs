using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;
using Azure.Storage.Blobs;

namespace Bitwarden.Server.Sdk.Licensing;

/// <summary>
/// Provides access to the certificate used to sign generated licenses.
/// </summary>
public interface ISigningCertificateProvider
{
    /// <summary>
    /// Whether or not signing licenses is supported in this environment.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Returns the signing certificate.
    /// </summary>
    /// <returns>The certificate, including its private key, used to sign licenses.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when the current environment cannot sign licenses (i.e. <see cref="IsSupported"/> is
    /// <see langword="false"/>).
    /// </exception>
    X509Certificate2 Get();
}

/// <summary>
/// Default implementation of <see cref="ISigningCertificateProvider"/> that returns a single,
/// pre-loaded certificate.
/// </summary>
public sealed class SigningCertificateProvider : ISigningCertificateProvider
{
    private readonly X509Certificate2 _certificate;

    /// <summary>
    /// Initializes a new instance backed by the supplied certificate.
    /// </summary>
    /// <param name="certificate">The certificate returned from <see cref="Get"/>. Must contain a private key.</param>
    public SigningCertificateProvider(X509Certificate2 certificate)
    {
        _certificate = certificate;
    }

    /// <inheritdoc />
    /// <remarks>
    /// This method unconditionally returns <see langword="true"/>.
    /// </remarks>
    public bool IsSupported => true;

    /// <inheritdoc />
    public X509Certificate2 Get() => _certificate;

    /// <summary>
    /// Attempts to locate a certificate in the current user's <c>My</c> store by thumbprint.
    /// </summary>
    /// <param name="thumbprint">The SHA-1 thumbprint to search for.</param>
    /// <param name="password">
    /// Optional password used to unlock the certificate's private key. When supplied, the located
    /// certificate is round-tripped through PFX with this password so the returned instance's
    /// private key is bound to it. Pass <see langword="null"/> when the key is not
    /// password-protected.
    /// </param>
    /// <param name="certificate">When this method returns <see langword="true"/>, the located certificate; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if a matching certificate was found; otherwise <see langword="false"/>.</returns>
    public static bool TryGetFromCertificateStore(string thumbprint, string? password, [MaybeNullWhen(false)] out X509Certificate2 certificate)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        var filtered = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
        if (filtered.Count > 0)
        {
            var found = filtered[0];
            if (string.IsNullOrEmpty(password))
            {
                certificate = found;
            }
            else
            {
                var pfx = found.Export(X509ContentType.Pfx, password);
                certificate = new X509Certificate2(pfx, password, X509KeyStorageFlags.Exportable);
            }
            return true;
        }

        certificate = null;
        return false;
    }
}

internal sealed class NotSupportedSigningCertificateProvider : ISigningCertificateProvider
{
    public bool IsSupported => false;

    public X509Certificate2 Get() => throw new NotSupportedException("Signing licenses is not supported in the current environment.");
}

internal abstract class RemoteSigningCertificateProvider : ISigningCertificateProvider, IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private X509Certificate2? _cachedCertificate;

    public bool IsSupported => true;

    public X509Certificate2 Get() => GetAsync(CancellationToken.None).GetAwaiter().GetResult();

    public Task<X509Certificate2> GetAsync(CancellationToken cancellationToken)
    {
        if (_cachedCertificate is not null)
            return Task.FromResult(_cachedCertificate);

        return FetchAsync(cancellationToken);

        async Task<X509Certificate2> FetchAsync(CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                _cachedCertificate ??= await GetCoreAsync(cancellationToken);
                return _cachedCertificate;
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cachedCertificate?.Dispose();
            _lock.Dispose();
        }
    }

    protected abstract Task<X509Certificate2> GetCoreAsync(CancellationToken cancellationToken);
}

internal sealed class AzureBlobSigningCertificateProvider : RemoteSigningCertificateProvider
{
    private readonly string _connectionString;
    private readonly string _fileName;
    private readonly string _containerName;
    private readonly string? _password;
    private readonly BlobClientOptions _blobClientOptions;

    public AzureBlobSigningCertificateProvider(
        string connectionString,
        string fileName,
        string containerName,
        string? password,
        BlobClientOptions blobClientOptions)
    {
        _connectionString = connectionString;
        _fileName = fileName;
        _containerName = containerName;
        _password = password;
        _blobClientOptions = blobClientOptions;
        new BlobClientOptions();
    }

    protected override async Task<X509Certificate2> GetCoreAsync(CancellationToken cancellationToken)
    {
        var blobServiceClient = new BlobServiceClient(_connectionString, _blobClientOptions);
        var containerRef = blobServiceClient.GetBlobContainerClient(_containerName);
        var blobRef = containerRef.GetBlobClient(_fileName);

        using var memoryStream = new MemoryStream();
        await blobRef.DownloadToAsync(memoryStream, cancellationToken);
        return new X509Certificate2(memoryStream.ToArray(), _password);
    }
}
