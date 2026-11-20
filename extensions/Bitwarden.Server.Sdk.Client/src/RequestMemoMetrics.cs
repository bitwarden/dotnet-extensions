using System.Diagnostics.Metrics;
using Bitwarden.Server.Sdk.Context;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Counts what the request memo answered: each call a service client made while a memo was current, and each read of an
/// owner's cache beside it, by where its answer came from. Never what was asked or answered.
/// </summary>
internal static class RequestMemoMetrics
{
    /// <summary>
    /// The meter's name, collected with the platform's other <c>Bitwarden.*</c> meters.
    /// </summary>
    internal const string MeterName = "Bitwarden.Server.Sdk.Client.RequestMemo";

    /// <summary>
    /// The counter's name.
    /// </summary>
    internal const string ReadsName = "bitwarden.request_memo.reads";

    /// <summary>
    /// The tag naming the service called, as its client is registered.
    /// </summary>
    internal const string ServiceTag = "bitwarden.client.service";

    /// <summary>
    /// The tag saying what was read: <see cref="CallSource"/> or <see cref="CacheSource"/>.
    /// </summary>
    internal const string SourceTag = "bitwarden.request_memo.source";

    /// <summary>
    /// The tag saying where the answer came from: <c>hit</c>, <c>joined</c>, <c>miss</c> or <c>bypassed</c>.
    /// </summary>
    internal const string OutcomeTag = "bitwarden.request_memo.outcome";

    /// <summary>
    /// A call to the service.
    /// </summary>
    internal const string CallSource = "call";

    /// <summary>
    /// A read of the service's owned cache.
    /// </summary>
    internal const string CacheSource = "cache";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Reads = Meter.CreateCounter<long>(
        ReadsName,
        unit: "{read}",
        description: "Calls to other services, and reads of their caches, made while a request memo was current, by where the answer came from.");

    public static void Counted(string service, string source, RequestMemoOutcome outcome) =>
        Reads.Add(
            1,
            new KeyValuePair<string, object?>(ServiceTag, service),
            new KeyValuePair<string, object?>(SourceTag, source),
            new KeyValuePair<string, object?>(OutcomeTag, outcome switch
            {
                RequestMemoOutcome.Hit => "hit",
                RequestMemoOutcome.Joined => "joined",
                RequestMemoOutcome.Miss => "miss",
                _ => "bypassed",
            }));
}
