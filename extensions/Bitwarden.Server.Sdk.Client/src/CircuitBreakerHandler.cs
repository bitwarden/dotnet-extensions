using Bitwarden.Server.Sdk.Errors;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Refuses a call to a callee whose circuit is open (<see cref="CalleeCircuit"/>) with the
/// <see cref="ServiceUnavailableException"/> its being down would have produced, so callers answer as they do today,
/// without waiting for a connection or a deadline; and reports each attempt's outcome to the circuit.
/// </summary>
/// <remarks>
/// Innermost, so it sees each attempt at the callee itself, after the retries outside it and after the call's tokens
/// are obtained: Identity failing never counts against the callee.
/// </remarks>
/// <param name="service">The callee, as its client registers it.</param>
/// <param name="circuit">The callee's circuit.</param>
internal sealed class CircuitBreakerHandler(string service, CalleeCircuit circuit) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!circuit.TryEnter(out var probe))
        {
            throw new ServiceUnavailableException(
                service,
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"failed {circuit.BreakAfter} calls in a row and is not called again for up to {circuit.BreakFor.TotalSeconds:0.###} seconds"));
        }

        HttpResponseMessage response;

        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException failure) when (Transient.Is(failure))
        {
            circuit.Failed(probe);
            throw;
        }
        catch (OperationCanceledException) when (ServiceFaultHandler.DeadlinePassed(request))
        {
            circuit.Failed(probe);
            throw;
        }
        catch
        {
            circuit.Abandoned(probe);
            throw;
        }

        if (Transient.Is(response))
        {
            circuit.Failed(probe);
        }
        else
        {
            circuit.Succeeded();
        }

        return response;
    }
}
