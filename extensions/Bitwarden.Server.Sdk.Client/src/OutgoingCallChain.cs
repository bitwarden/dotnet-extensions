using Bitwarden.Server.Sdk.Context;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Writes <see cref="BitwardenHeaders.CallChain"/> on a call to another service: the chain the current request arrived
/// with (<see cref="CallChain.Current"/>), this service's own name added last.
/// </summary>
/// <remarks>
/// <para>
/// So the callee, and every service it calls in turn, can tell when a call comes back into a service whose own request
/// is still waiting on it (a re-entry). The receiving side is the Service package's call chain check.
/// </para>
/// <para>
/// Whatever a call site put in the header is removed first, as the context headers are: only this writes it.
/// A process that isn't a service (no <see cref="IServiceIdentity"/>, such as a tool) has no name to add and sends
/// none.
/// </para>
/// </remarks>
internal static class OutgoingCallChain
{
    /// <summary>
    /// Replaces the request's call chain with the current one, this service added.
    /// </summary>
    /// <param name="request">The outgoing call.</param>
    /// <param name="services">A scope's services, for this service's identity.</param>
    public static void Write(HttpRequestMessage request, IServiceProvider services)
    {
        request.Headers.Remove(BitwardenHeaders.CallChain);

        if (services.GetService<IServiceIdentity>()?.Name is not { } self || !CallChain.IsServiceName(self))
        {
            return;
        }

        request.Headers.TryAddWithoutValidation(
            BitwardenHeaders.CallChain, CallChain.Format(CallChain.Append(CallChain.Current, self)));
    }
}
