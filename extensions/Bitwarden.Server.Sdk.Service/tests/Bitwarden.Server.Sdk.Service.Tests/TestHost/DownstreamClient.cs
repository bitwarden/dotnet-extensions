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
    public async Task CallAsync(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync("api/v1/things", cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
