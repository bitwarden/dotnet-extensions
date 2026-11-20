namespace Bitwarden.Server.Sdk.Context;

/// <summary>
/// Where an answer came from.
/// </summary>
internal enum RequestMemoOutcome
{
    /// <summary>
    /// Remembered from an earlier call.
    /// </summary>
    Hit,

    /// <summary>
    /// From an identical call in flight at the same time.
    /// </summary>
    Joined,

    /// <summary>
    /// Read now.
    /// </summary>
    Miss,

    /// <summary>
    /// Read now without the memo: past its bounds, after it ended, or the shared read couldn't serve.
    /// </summary>
    Bypassed,
}
