using System.Net;
using System.Net.Sockets;
using System.Text;
using MySqlConnector;

namespace Bitwarden.Server.Sdk.Database.Analyzers.Tests;

/// <summary>
/// Just enough of a MySQL server for <c>ServerVersion.AutoDetect</c> to read its version: it sends
/// the initial handshake, accepts whatever credentials come back, and answers each later command
/// with OK. It counts the connections it accepts, which is how a test sees how often a server was
/// asked its version.
/// </summary>
/// <remarks>
/// Nothing is authenticated: the client's credential response is read and discarded.
/// </remarks>
internal sealed class FakeMySqlServer : IAsyncDisposable
{
    // CLIENT_LONG_PASSWORD | CLIENT_CONNECT_WITH_DB | CLIENT_PROTOCOL_41 | CLIENT_TRANSACTIONS
    // | CLIENT_SECURE_CONNECTION | CLIENT_PLUGIN_AUTH. No CLIENT_SSL, so the client stays on plain TCP.
    private const uint Capabilities = 0x1 | 0x8 | 0x200 | 0x2000 | 0x8000 | 0x80000;

    private const byte ComQuit = 0x01;

    // An OK packet: no rows affected, no insert id, autocommit on, no warnings.
    private static readonly byte[] Ok = [0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00];

    private readonly TcpListener _listener;
    private readonly string _version;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _accepting;
    private int _connections;
    private volatile bool _refuseConnections;

    private FakeMySqlServer(string version)
    {
        _version = version;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _accepting = AcceptAsync();
    }

    /// <summary>
    /// Starts a server on a free loopback port that reports the given version.
    /// </summary>
    public static FakeMySqlServer Start(string version = "8.4.0") => new(version);

    /// <summary>
    /// The number of connections accepted so far, refused ones included.
    /// </summary>
    public int Connections => Volatile.Read(ref _connections);

    /// <summary>
    /// While set, each connection is closed before the handshake, as a server that isn't ready yet
    /// would, so the client's open fails.
    /// </summary>
    public bool RefuseConnections
    {
        get => _refuseConnections;
        set => _refuseConnections = value;
    }

    /// <summary>
    /// A connection string for this server.
    /// </summary>
    public string ConnectionString(string database, string user = "app") => new MySqlConnectionStringBuilder
    {
        Server = "127.0.0.1",
        Port = (uint)((IPEndPoint)_listener.LocalEndpoint).Port,
        Database = database,
        UserID = user,
        Password = "not-checked",
        SslMode = MySqlSslMode.None,
        ConnectionTimeout = 5,
    }.ConnectionString;

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _accepting;
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            Interlocked.Increment(ref _connections);
            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            if (_refuseConnections)
                return;

            try
            {
                var stream = client.GetStream();
                await WritePacketAsync(stream, 0, Handshake());

                // The handshake response, whatever its credentials.
                if (await ReadPacketAsync(stream) is not { } response)
                    return;
                await WritePacketAsync(stream, (byte)(response.Sequence + 1), Ok);

                while (await ReadPacketAsync(stream) is { } command && command.Payload[0] != ComQuit)
                    await WritePacketAsync(stream, (byte)(command.Sequence + 1), Ok);
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
            {
                // The client went away; there is nothing to answer.
            }
        }
    }

    /// <summary>
    /// The protocol 10 initial handshake, offering mysql_native_password.
    /// </summary>
    private byte[] Handshake()
    {
        // The challenge the client answers. Fixed, since the answer is never checked.
        var challenge = Enumerable.Range(1, 20).Select(i => (byte)i).ToArray();

        var payload = new List<byte> { 10 };
        payload.AddRange(Encoding.ASCII.GetBytes(_version));
        payload.Add(0);
        payload.AddRange([1, 0, 0, 0]); // connection id
        payload.AddRange(challenge[..8]);
        payload.Add(0);
        payload.Add((byte)(Capabilities & 0xFF));
        payload.Add((byte)((Capabilities >> 8) & 0xFF));
        payload.Add(255); // utf8mb4_0900_ai_ci
        payload.AddRange([0x02, 0x00]); // SERVER_STATUS_AUTOCOMMIT
        payload.Add((byte)((Capabilities >> 16) & 0xFF));
        payload.Add((byte)((Capabilities >> 24) & 0xFF));
        payload.Add((byte)(challenge.Length + 1));
        payload.AddRange(new byte[10]);
        payload.AddRange(challenge[8..]);
        payload.Add(0);
        payload.AddRange(Encoding.ASCII.GetBytes("mysql_native_password"));
        payload.Add(0);
        return [.. payload];
    }

    private static async Task WritePacketAsync(NetworkStream stream, byte sequence, byte[] payload)
    {
        byte[] header = [(byte)payload.Length, (byte)(payload.Length >> 8), (byte)(payload.Length >> 16), sequence];
        await stream.WriteAsync(header);
        await stream.WriteAsync(payload);
    }

    private static async Task<(byte Sequence, byte[] Payload)?> ReadPacketAsync(NetworkStream stream)
    {
        var header = new byte[4];
        if (!await ReadExactlyOrEndAsync(stream, header))
            return null;

        var payload = new byte[header[0] | header[1] << 8 | header[2] << 16];
        if (!await ReadExactlyOrEndAsync(stream, payload))
            return null;

        return (header[3], payload);
    }

    private static async Task<bool> ReadExactlyOrEndAsync(NetworkStream stream, byte[] buffer)
    {
        try
        {
            await stream.ReadExactlyAsync(buffer);
            return true;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }
}
