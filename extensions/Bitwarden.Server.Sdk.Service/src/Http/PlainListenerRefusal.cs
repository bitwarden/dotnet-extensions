using Bitwarden.Server.Sdk.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Refuses plain-HTTP listeners, before the server listens (its configuration) and once it does (its addresses).
/// </summary>
internal sealed class PlainListenerRefusal(IConfiguration configuration, IServer? server, int? healthPort) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        Refuse(InternalTlsServer.ConfiguredAddresses(configuration), "is configured to listen on");
        return Task.CompletedTask;
    }

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        if (server?.Features.Get<IServerAddressesFeature>() is { } listening)
        {
            Refuse(listening.Addresses, "listens on");
        }

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Refuse(IEnumerable<string> addresses, string verb)
    {
        var plain = InternalTlsServer.PlainAddresses(addresses, healthPort).ToList();
        if (plain.Count > 0)
        {
            var exception = healthPort is { } port ? $" but {InternalTls.Section}:HealthPort, {port}" : string.Empty;
            throw new InvalidOperationException(
                $"{InternalTls.Section}:RequireHttps is set, and the service {verb} plain HTTP: {string.Join(", ", plain)}. "
                + $"Listen on https:// only{exception}.");
        }
    }
}
