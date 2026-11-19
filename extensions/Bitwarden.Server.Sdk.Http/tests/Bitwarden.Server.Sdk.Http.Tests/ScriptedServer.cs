using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Bitwarden.Server.Sdk.Http.Tests;

/// <summary>
/// A server on the loopback address answering each request with the next of its answers, or 200 once they run out,
/// one request per connection, and keeping each request's head as it arrived.
/// </summary>
internal sealed class ScriptedServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentQueue<string> _answers;
    private readonly ConcurrentQueue<string> _requests = new();

    /// <param name="answers">Each answer's status line and headers, without the blank line ending them.</param>
    public ScriptedServer(params string[] answers)
    {
        _answers = new ConcurrentQueue<string>(answers);
        _listener.Start();
        _ = ServeAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>
    /// Each request's request line and headers, one per line.
    /// </summary>
    public IReadOnlyList<string> Requests => [.. _requests];

    private async Task ServeAsync()
    {
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                using var connection = await _listener.AcceptTcpClientAsync(_stopping.Token);
                var stream = connection.GetStream();
                var reader = new StreamReader(stream, Encoding.ASCII);
                var head = new StringBuilder();
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(_stopping.Token)))
                {
                    head.Append(line).Append('\n');
                }

                _requests.Enqueue(head.ToString());
                var answer = _answers.TryDequeue(out var next) ? next : "HTTP/1.1 200 OK";
                await stream.WriteAsync(
                    Encoding.ASCII.GetBytes($"{answer}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), _stopping.Token);
            }
        }
        catch (Exception) when (_stopping.IsCancellationRequested)
        {
            // Stopped.
        }
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _listener.Stop();
        _stopping.Dispose();
    }
}
