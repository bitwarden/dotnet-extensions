using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Bitwarden.Server.Sdk.Idempotency;

/// <summary>
/// Names one logical operation, so that when it is run again its writes carry the same idempotency keys and each
/// callee that accepts keys does each of them at most once.
/// </summary>
/// <remarks>
/// <para>
/// A client that sends an <c>Idempotency-Key</c> (<see cref="IdempotencyKeys.Header"/>) on a write, to an operation
/// whose service's contract marks it (<see cref="IdempotencyKeys.Extension"/>), asks <see cref="KeyForWrite"/> for it. Outside an operation the client sends a fresh key for each call, which covers its
/// own retries of that call. Inside one, the key is derived from the operation's key, the method, the address, the body, and how many identical writes the
/// operation has made before: so an operation that is run again from the start (a message redelivered, a job's
/// occurrence run again, an attempt of a retry loop) with the same key sends the same keys, and the callees replay what
/// they answered the first time. A write whose body differs from the first run's (it carries a time, or reads
/// something the first run changed) gets a key of its own and is done as a new write, as it would be outside an
/// operation; it is never refused for differing.
/// </para>
/// <para>
/// The key must be stable for the logical operation and unique to it: a message's id, a job occurrence, an id the
/// operation mints once before its first attempt. Never a person's data: no email address, no name.
/// </para>
/// <para>
/// A host that runs message handlers or jobs can begin one around each run, keyed by <see cref="ForMessage"/> or
/// <see cref="ForJob"/>, and leave out a handler or job that declares <see cref="PerCallIdempotencyKeysAttribute"/>.
/// A retry loop begins one per attempt, with a key minted before the loop.
/// </para>
/// <code>
/// using (IdempotentOperation.Begin($"archive-widget:{message.Id}"))
/// {
///     await widgets.ArchiveAsync(...);
///     await audit.RecordAsync(...);
/// }
/// </code>
/// </remarks>
public static class IdempotentOperation
{
    private static readonly AsyncLocal<Scope?> Ambient = new();

    /// <summary>
    /// The longest operation key accepted.
    /// </summary>
    public const int MaximumKeyLength = 200;

    /// <summary>
    /// Begins an operation: the writes made until the result is disposed carry keys derived from
    /// <paramref name="key"/>, and <see cref="DeriveId"/> answers from it.
    /// </summary>
    /// <param name="key">Stable for the logical operation, and unique to it.</param>
    /// <returns>
    /// Ends the operation, restoring any operation it was begun inside. An ended operation is never current again, even
    /// when operations are ended out of order.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> is blank, too long, or not visible ASCII.</exception>
    /// <remarks>
    /// Begin it in the method that makes the writes, or one that awaits it: an ambient value set inside an awaited
    /// method is not seen by its caller.
    /// </remarks>
    public static IDisposable Begin(string key)
    {
        if (!IdempotencyKeys.IsValid(key) || key.Length > MaximumKeyLength)
        {
            throw new ArgumentException(
                $"An operation key is 1 to {MaximumKeyLength} visible ASCII characters.", nameof(key));
        }

        var scope = new Scope(key, Ambient.Value);
        Ambient.Value = scope;

        return scope;
    }

    /// <summary>
    /// The operation's key, when one has begun.
    /// </summary>
    public static string? Current => Active?.Key;

    private static Scope? Active => Ambient.Value is { Ended: false } scope ? scope : null;

    /// <summary>
    /// The operation key a message handler's work runs under: the handler's name and the envelope's identifier, so a
    /// redelivery repeats it and two handlers of one envelope don't share it.
    /// </summary>
    /// <param name="handler">The handler's stable name, such as its type's full name.</param>
    /// <param name="envelope">The envelope's identifier.</param>
    /// <returns>The key; a long name is shortened to its hash.</returns>
    /// <exception cref="ArgumentException"><paramref name="handler"/> is null, empty or white space.</exception>
    public static string ForMessage(string handler, Guid envelope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handler);

        return Compose(handler, envelope.ToString());
    }

    /// <summary>
    /// The operation key a recurring job's run works under: the job's name and the occurrence's due time, so the same
    /// occurrence run again (a lease that lapsed, a service stopped mid-run) repeats it, and the next occurrence doesn't.
    /// </summary>
    /// <param name="job">The job's name.</param>
    /// <param name="dueAt">When the occurrence was due.</param>
    /// <returns>The key; a long name is shortened to its hash.</returns>
    /// <exception cref="ArgumentException"><paramref name="job"/> is null, empty or white space.</exception>
    public static string ForJob(string job, DateTimeOffset dueAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(job);

        return Compose($"job:{job}", dueAt.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The key for one write inside the current operation, or null outside one.
    /// </summary>
    /// <param name="method">The request's method.</param>
    /// <param name="address">The request's address.</param>
    /// <param name="body">The request's body; empty when it has none.</param>
    /// <returns>A lower-case hex SHA-256, or null when no operation has begun.</returns>
    public static string? KeyForWrite(string method, string address, ReadOnlySpan<byte> body)
    {
        if (Active is not { } scope)
        {
            return null;
        }

        var target = $"write\n{method}\n{address}\n{Convert.ToHexStringLower(SHA256.HashData(body))}";

        return Hash(scope.Key, target, scope.Next(target));
    }

    /// <summary>
    /// An identifier for something the operation creates or sends, derived from the operation, or null outside one:
    /// the same on every run of the operation, so a run again names the same thing.
    /// </summary>
    /// <param name="purpose">Tells this identifier from the operation's others, such as a template and its recipients.</param>
    /// <returns>A version 8 UUID (RFC 9562), or null when no operation has begun.</returns>
    /// <remarks>
    /// The purpose is hashed with the operation's key and never kept, so it may name what the identifier is for. The
    /// same purpose asked for twice in one run gets two identifiers, in order.
    /// </remarks>
    public static Guid? DeriveId(string purpose)
    {
        ArgumentNullException.ThrowIfNull(purpose);

        if (Active is not { } scope)
        {
            return null;
        }

        var target = $"id\n{purpose}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{scope.Key}\n{target}\n{scope.Next(target)}")).AsSpan(0, 16).ToArray();

        // RFC 9562 version 8 (custom) and the RFC variant, in the big-endian byte order the RFC writes.
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }

    private static string Compose(string name, string occurrence)
    {
        var key = $"{name}:{occurrence}";
        if (key.Length <= MaximumKeyLength && IdempotencyKeys.IsValid(key))
        {
            return key;
        }

        return $"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)))}:{occurrence}";
    }

    private static string Hash(string key, string target, int ordinal) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{key}\n{target}\n{ordinal}")));

    private sealed class Scope(string key, Scope? outer) : IDisposable
    {
        private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);

        public string Key { get; } = key;

        public bool Ended { get; private set; }

        /// <summary>
        /// How many times the operation has asked for <paramref name="target"/> before.
        /// </summary>
        public int Next(string target) => _counts.AddOrUpdate(target, 0, (_, count) => count + 1);

        public void Dispose()
        {
            if (Ended)
            {
                return;
            }

            Ended = true;
            Ambient.Value = outer;
        }
    }
}
