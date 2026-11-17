namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// How the service-owned cache answered.
/// </summary>
public enum CachedAnswerKind
{
    /// <summary>
    /// It could not: ask the service.
    /// </summary>
    Miss,

    /// <summary>
    /// With the resource.
    /// </summary>
    Hit,

    /// <summary>
    /// That nothing is recorded under the key (a read by a unique key only).
    /// </summary>
    NothingRecorded,
}
