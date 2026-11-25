namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// A product service's answer to one call made with somebody's own token, kept as it came: its status and its body.
/// </summary>
/// <remarks>
/// <para>
/// A product service answers people, not services, so its client is often called by something that translates the
/// answer for somebody else, writing it back in another API's shapes, its refusals included. That needs the answer as
/// the service wrote it, never an exception standing in for it, so a refusal is an answer here too.
/// A service that couldn't be reached, or didn't answer in time, is the exception the <see cref="HttpClient"/> threw.
/// </para>
/// <para>
/// The body may carry Protected Data (an item's encrypted fields, a wrapped key): <see cref="ToString"/> never names it,
/// and nothing here logs it.
/// </para>
/// </remarks>
/// <param name="Status">The HTTP status.</param>
/// <param name="Body">The body as text, read as the service's charset says; empty when there was none.</param>
public record ProductAnswer(int Status, string Body)
{
    /// <summary>
    /// Whether the status is 2xx.
    /// </summary>
    public bool Succeeded => Status is >= 200 and < 300;

    /// <summary>
    /// Names the status, never the body.
    /// </summary>
    /// <returns>The answer's type and status.</returns>
    public sealed override string ToString() => $"{GetType().Name} {{ Status = {Status} }}";
}
