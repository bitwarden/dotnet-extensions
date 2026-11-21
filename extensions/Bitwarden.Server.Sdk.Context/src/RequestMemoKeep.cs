namespace Bitwarden.Server.Sdk.Context;

/// <summary>
/// What a memo may do with an answer.
/// </summary>
internal enum RequestMemoKeep
{
    /// <summary>
    /// Keep it for the rest of the work, if it fits, and share it with callers waiting now.
    /// </summary>
    Keep,

    /// <summary>
    /// Share it with callers waiting now, and keep nothing: a failure each of them gets.
    /// </summary>
    Share,

    /// <summary>
    /// Neither: each waiting caller reads for itself.
    /// </summary>
    Alone,
}
