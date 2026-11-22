using Bitwarden.Server.Sdk.Sagas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Service.Sagas;

/// <summary>
/// Logs, counts and, with <see cref="SpansServicesOptions.RefuseUndeclared"/>, refuses an operation whose writes span
/// services without a declaration (<see cref="SpansServicesAttribute"/>). One that admits a known defect
/// (<see cref="SpansServicesUnhandledAttribute"/>) is logged and counted as loudly, never refused.
/// </summary>
/// <remarks>
/// The log names the operation and where its writes went, service names and <see cref="ServiceWrites.OwnDatabase"/>
/// only, never a value. A declared one is counted and logged at debug.
/// </remarks>
internal sealed class SpansServicesGuard(
    IOptionsMonitor<SpansServicesOptions> options,
    WriteSpanMetrics metrics,
    ILogger<SpansServicesGuard> logger) : IServiceWritesGuard
{
    /// <summary>
    /// Where a newcomer reads what to do.
    /// </summary>
    internal const string Guide =
        "Read the Bitwarden.Server.Sdk.Sagas PACKAGE.md to choose the shape";

    public void Spanned(string operation, IReadOnlyList<string> steps, SpanDeclarationAttribute? declared)
    {
        metrics.Spanned(operation, declared is SpansServicesAttribute, declared is SpansServicesUnhandledAttribute);
        var writes = string.Join(", then ", steps);

        if (declared is SpansServicesAttribute shaped)
        {
            logger.LogDebug(
                "{Operation} writes in more than one place ({Writes}), declared {Shape}.",
                operation,
                writes,
                shaped.Shape);
            return;
        }

        // An admitted defect: as loud as an undeclared span everywhere, only never refused.
        if (declared is SpansServicesUnhandledAttribute unhandled)
        {
            logger.LogWarning(
                "Unhandled writes across services, a known defect: {Operation} wrote {Writes}, so a failure part way leaves it half done. {Reason}",
                operation,
                writes,
                unhandled.Reason);
            return;
        }

        logger.LogWarning(
            "Undeclared writes across services: {Operation} wrote {Writes}, so a failure part way leaves it half done. Declare how it handles that with [SpansServices].",
            operation,
            writes);

        if (options.CurrentValue.RefuseUndeclared)
        {
            throw new UndeclaredSpanException(
                $"Refused a write of {operation}: it writes in more than one place ({writes}), at least one of them "
                + "another service called synchronously, and doesn't say how it handles a failure part way. "
                + "Nothing rolls such writes back together, so a failure after the first leaves the operation half done. "
                + "Decide how this operation recovers, make the code do it, then declare it on the endpoint's handler "
                + "method (or the message handler's, outbox handler's or job's class): "
                + "[SpansServices(SpanShape.X, \"how it does it, and the decision\")], where X is Compensated, "
                + "IrreversibleLast, RetrySafeByState or StageThenCommit (combine with |). Never declare a shape the code "
                + "doesn't implement. If the later writes can wait until after the commit, send them through the outbox "
                + "instead and nothing needs declaring. "
                + $"{Guide}. SpansServices:RefuseUndeclared is on.");
        }
    }
}
