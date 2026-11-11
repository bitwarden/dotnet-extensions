using System.Diagnostics.Metrics;

namespace Bitwarden.Server.Sdk.Service.Sagas;

/// <summary>
/// Counts operations whose writes spanned services: two or more writes, at least one to another service.
/// </summary>
internal sealed class WriteSpanMetrics
{
    /// <summary>
    /// The meter's name, collected with the platform's other <c>Bitwarden.*</c> meters.
    /// </summary>
    internal const string MeterName = "Bitwarden.Server.Sdk.Service.Sagas";

    /// <summary>
    /// The counter's name.
    /// </summary>
    internal const string SpansName = "bitwarden.operation.spans";

    /// <summary>
    /// The tag naming the operation: an endpoint's route template or a handler's or job's name, never a path.
    /// </summary>
    internal const string OperationTag = "bitwarden.operation";

    /// <summary>
    /// The tag saying whether the operation declares how it handles a failure part way.
    /// </summary>
    internal const string DeclaredTag = "bitwarden.operation.spans.declared";

    /// <summary>
    /// The tag saying whether the operation admits it doesn't handle a failure part way yet, a known defect.
    /// </summary>
    internal const string UnhandledTag = "bitwarden.operation.spans.unhandled";

    private readonly Counter<long> _spans;

    public WriteSpanMetrics(IMeterFactory meters)
    {
        _spans = meters.Create(MeterName).CreateCounter<long>(
            SpansName,
            unit: "{operation}",
            description: "Operations whose writes spanned services: two or more writes, at least one to another service.");
    }

    public void Spanned(string operation, bool declared, bool unhandled) =>
        _spans.Add(
            1,
            new KeyValuePair<string, object?>(OperationTag, operation),
            new KeyValuePair<string, object?>(DeclaredTag, declared),
            new KeyValuePair<string, object?>(UnhandledTag, unhandled));
}
