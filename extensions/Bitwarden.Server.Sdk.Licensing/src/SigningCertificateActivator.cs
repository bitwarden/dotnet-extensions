using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.Licensing;


internal sealed class SigningCertificateActivator : IHostedService
{
    private readonly ISigningCertificateProvider _signingCertificateProvider;

    public SigningCertificateActivator(ISigningCertificateProvider signingCertificateProvider)
    {
        _signingCertificateProvider = signingCertificateProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Likely self-hosted
        if (!_signingCertificateProvider.IsSupported)
        {
            return Task.CompletedTask;
        }

        if (_signingCertificateProvider is RemoteSigningCertificateProvider remoteSigningCertificateProvider)
        {
            return remoteSigningCertificateProvider.GetAsync(cancellationToken);
        }

        _ = _signingCertificateProvider.Get();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
