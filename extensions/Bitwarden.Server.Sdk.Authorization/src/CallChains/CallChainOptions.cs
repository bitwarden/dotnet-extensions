namespace Bitwarden.Server.Sdk.Authorization.CallChains;

/// <summary>
/// What a service does when it is called back while a request of its own waits on the call (<c>CallChain:*</c>).
/// </summary>
public sealed class CallChainOptions
{
    /// <summary>
    /// The configuration section.
    /// </summary>
    public const string SectionName = "CallChain";

    /// <summary>
    /// Refuse an undeclared re-entry (a <c>500</c>) rather than only log and count it. Off by default. Turn it on in
    /// tests and in Development, so a new loop-back fails a test or a local run instead of waiting for an audit.
    /// </summary>
    public bool RefuseReentry { get; set; }
}
