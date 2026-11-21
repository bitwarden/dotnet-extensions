using Microsoft.Extensions.Configuration;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// How long a read may take to fill the cache, and how long a change may take to commit once it has invalidated it.
/// </summary>
/// <remarks>
/// <para>
/// A change invalidates its entries just before it commits, so a tombstone is written before the change can be read. A
/// tombstone protects only while it is there: it must outlive every fill that read before the change committed.
/// Such a fill is at most <see cref="FillLimit"/> old, measured from just before its read, and its read began at most
/// <see cref="CommitAllowance"/> after the tombstone was written. So every tombstone must outlive the two together,
/// which the host checks as it starts.
/// </para>
/// <para>
/// A row arriving under a key several rows may hold marks every version dead (<see cref="Invalidation.Arrival"/>), and
/// that tombstone lives <see cref="CommitAllowance"/>: it names no version, so it must refuse every fill read before
/// the arrival committed, and those arrive within the allowance. A read after the commit finds
/// the arrival, and fills the one row at its own version, or nothing while two rows hold the key. Every other
/// tombstone keeps the full lifetime.
/// </para>
/// </remarks>
internal sealed class OwnedCacheTimes
{
    /// <summary>
    /// The section both are read from.
    /// </summary>
    public const string Section = "Caching:Owned";

    /// <summary>
    /// How long after its database read began a fill is still written: <c>Caching:Owned:FillLimit</c>, five seconds
    /// unless configured. A slower read is answered, but not kept.
    /// </summary>
    public TimeSpan FillLimit { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long after its invalidation a change is assumed to have committed or rolled back:
    /// <c>Caching:Owned:CommitAllowance</c>, a minute unless configured. Must exceed the database's command timeout
    /// plus <see cref="FillLimit"/>.
    /// </summary>
    public TimeSpan CommitAllowance { get; set; } = TimeSpan.FromSeconds(60);

    public static OwnedCacheTimes From(IConfiguration configuration)
    {
        var times = new OwnedCacheTimes();
        configuration.GetSection(Section).Bind(times);
        return times;
    }
}
