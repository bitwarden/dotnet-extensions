using Microsoft.AspNetCore.Http.Features;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// The connection's lifetime, without the client's leaving.
/// </summary>
internal sealed class Unabandoned(IHttpRequestLifetimeFeature connection) : IHttpRequestLifetimeFeature
{
    public CancellationToken RequestAborted
    {
        get => CancellationToken.None;
        set { }
    }

    public void Abort() => connection.Abort();
}
