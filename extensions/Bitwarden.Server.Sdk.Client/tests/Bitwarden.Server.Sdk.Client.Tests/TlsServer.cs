using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// A TLS server on the loopback address serving, for the name a client asks for, the certificate that names it (or
/// else the first) with its chain, answering every HTTP request with 200, and keeping the last name asked for.
/// </summary>
internal sealed class TlsServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly SslStreamCertificateContext[] _certificates;

    public TlsServer(params SslStreamCertificateContext[] certificates)
    {
        _certificates = certificates;
        _listener.Start();
        _ = ServeAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public string? ServerName { get; private set; }

    /// <summary>
    /// <paramref name="certificate"/>, with its private key, and the intermediates it is sent with.
    /// </summary>
    public static SslStreamCertificateContext Serving(X509Certificate2 certificate, params X509Certificate2[] intermediates) =>
        SslStreamCertificateContext.Create(certificate, [.. intermediates], offline: true);

    /// <summary>
    /// A client connecting to this server whatever address a request names, and trusting as
    /// <paramref name="policy"/> says.
    /// </summary>
    public SocketsHttpHandler Client(X509ChainPolicy policy, List<string>? connectedTo = null) => new()
    {
        ConnectCallback = async (context, cancellationToken) =>
        {
            connectedTo?.Add(context.DnsEndPoint.Host);
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(IPAddress.Loopback, Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        },
        SslOptions = new SslClientAuthenticationOptions { CertificateChainPolicy = policy },
    };

    /// <summary>
    /// A connect callback reaching this server whatever address a request names, for a handler set up elsewhere.
    /// </summary>
    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, Port, cancellationToken);
        return new NetworkStream(socket, ownsSocket: true);
    }

    private async Task ServeAsync()
    {
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                var connection = await _listener.AcceptTcpClientAsync(_stopping.Token);
                _ = AnswerAsync(connection);
            }
        }
        catch (Exception) when (_stopping.IsCancellationRequested)
        {
            // Stopped.
        }
    }

    private async Task AnswerAsync(TcpClient connection)
    {
        using (connection)
        await using (var tls = new SslStream(connection.GetStream()))
        {
            try
            {
                await tls.AuthenticateAsServerAsync(
                    (_, hello, _, _) =>
                    {
                        ServerName = hello.ServerName;
                        return ValueTask.FromResult(new SslServerAuthenticationOptions
                        {
                            ServerCertificateContext = _certificates.FirstOrDefault(
                                c => c.TargetCertificate.GetNameInfo(X509NameType.DnsName, false) == hello.ServerName) ?? _certificates[0],
                        });
                    },
                    null,
                    _stopping.Token);

                var buffer = new byte[4096];
                while (await tls.ReadAsync(buffer, _stopping.Token) > 0)
                {
                    await tls.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray(), _stopping.Token);
                }
            }
            catch (Exception) when (_stopping.IsCancellationRequested)
            {
                // Stopped.
            }
            catch (Exception exception) when (exception is IOException or System.Security.Authentication.AuthenticationException)
            {
                // The client went away, or refused the certificate.
            }
        }
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _listener.Stop();
        _stopping.Dispose();
    }
}
