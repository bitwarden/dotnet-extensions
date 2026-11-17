namespace Bitwarden.Server.Sdk.Service.Observability;

/// <summary>
/// Which signals a service sends over OTLP.
/// </summary>
/// <param name="Traces">Whether spans are exported.</param>
/// <param name="Metrics">Whether metrics are exported.</param>
/// <param name="Logs">Whether log records are exported.</param>
internal readonly record struct Exports(bool Traces, bool Metrics, bool Logs);
