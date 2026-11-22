namespace Bitwarden.Server.Sdk.Service.Tests.TestHost;

/// <summary>
/// A client for a domain service, as a generated one would be registered.
/// </summary>
/// <param name="http">Configured by the platform with the credential and the context.</param>
public sealed class DownstreamClient(HttpClient http)
{
    /// <summary>
    /// Makes one call.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task CallAsync(CancellationToken cancellationToken) => CallAsync("api/v1/things", cancellationToken);

    /// <summary>
    /// Reads <paramref name="path"/>.
    /// </summary>
    /// <param name="path">What to read, relative to the service.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task CallAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Writes to <paramref name="path"/>.
    /// </summary>
    /// <param name="path">What to write, relative to the service.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task WriteAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsync(path, new StringContent("{}"), cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
