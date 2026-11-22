using System.Collections.Concurrent;

namespace Bitwarden.Server.Sdk.Service.Tests.TestHost;

/// <summary>
/// What left the service, by where it went.
/// </summary>
public sealed class Recorder
{
    /// <summary>
    /// Calls to the downstream service, with the headers each carried.
    /// </summary>
    public ConcurrentQueue<IReadOnlyDictionary<string, string>> Downstream { get; } = new();

    /// <summary>
    /// The <c>client_id</c> each token request named. The secret is never kept.
    /// </summary>
    public ConcurrentQueue<string?> TokenRequests { get; } = new();

    /// <summary>
    /// Each context token exchange's form, but the secret.
    /// </summary>
    public ConcurrentQueue<Dictionary<string, string>> ContextExchanges { get; } = new();
}
