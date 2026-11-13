using Microsoft.AspNetCore.Http.Features;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// The connection's lifetime, without the client's leaving.
/// </summary>
/// <remarks>
/// Starts from <see cref="CancellationToken.None"/> and never copies in the connection's token, so the
/// client's leaving can't reach the handler. A token the pipeline assigns, such as a request timeout's,
/// is kept.
/// </remarks>
internal sealed class Unabandoned(IHttpRequestLifetimeFeature connection) : IHttpRequestLifetimeFeature
{
    public CancellationToken RequestAborted { get; set; } = CancellationToken.None;

    public void Abort() => connection.Abort();
}
