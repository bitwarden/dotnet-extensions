using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Bitwarden.Server.Sdk.CryptoPrimitives;

namespace Bitwarden.Server.Sdk.Idempotency;

/// <summary>
/// Names one logical operation, so that when it is run again its writes carry the same idempotency keys and each
/// callee that accepts keys does each of them at most once.
/// </summary>
/// <remarks>
/// <para>
/// A client that sends an <c>Idempotency-Key</c> (<see cref="IdempotencyKeys.Header"/>) on a write, to an operation
/// whose service's contract marks it (<see cref="IdempotencyKeys.Extension"/>), asks <see cref="KeyForWrite"/> for it. Outside an operation the client sends a fresh key for each call, which covers its
/// own retries of that call. Inside one, the key is derived, with the service's secret (<see cref="IdempotencySecret"/>), from the operation's key, the method, the address, the body, and how many identical writes the
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
/// A host that runs message handlers or jobs can begin one around each run, keyed by <see cref="ForMessage(string, string)"/> or
/// <see cref="ForJob"/>, and leave out a handler or job that declares <see cref="PerCallIdempotencyKeysAttribute"/>.
/// A retry loop begins one per attempt, with a key minted before the loop. Each begins it with the service's
/// <see cref="IdempotencySecret"/>.
/// </para>
/// <code>
/// using (IdempotentOperation.Begin($"archive-widget:{message.Id}", idempotencySecret))
/// {
///     await widgets.ArchiveAsync(...);
///     await audit.RecordAsync(...);
/// }
/// </code>
/// </remarks>
public static class IdempotentOperation
{
    private static readonly AsyncLocal<Scope?> Ambient = new();

    // The first parameter of each derivation, one per use, so a write key and a derived identifier never share an
    // input.
    private const string WriteKeyPurpose = "idempotency-key";

    private const string DerivedIdPurpose = "derived-id";

    // The first part of an operation key ForMessage or ForJob gives, so a message's key and a job's never match.
    private const string MessageKind = "msg";

    private const string JobKind = "job";

    /// <summary>
    /// The longest operation key accepted.
    /// </summary>
    public const int MaximumKeyLength = 200;

    /// <summary>
    /// Begins an operation: the writes made until the result is disposed carry keys derived from
    /// <paramref name="key"/> with <paramref name="secret"/>, and <see cref="DeriveId"/> answers from them.
    /// </summary>
    /// <param name="key">Stable for the logical operation, and unique to it.</param>
    /// <param name="secret">
    /// The service's secret (<see cref="IdempotencySecret"/>). A run again must begin with the same one, or its writes
    /// carry new keys and are done again.
    /// </param>
    /// <returns>
    /// Ends the operation, restoring the nearest operation it was begun inside that is still running. An ended operation
    /// is never current again, and one ended out of order leaves alone an operation begun since.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> is blank, too long, or not visible ASCII.</exception>
    /// <remarks>
    /// Begin it in the method that makes the writes, or one that awaits it: an ambient value set inside an awaited
    /// method is not seen by its caller.
    /// </remarks>
    public static IDisposable Begin(string key, IdempotencySecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        if (!IdempotencyKeys.IsValid(key) || key.Length > MaximumKeyLength)
        {
            throw new ArgumentException(
                $"An operation key is 1 to {MaximumKeyLength} visible ASCII characters.", nameof(key));
        }

        var scope = new Scope(key, secret.Value, Ambient.Value);
        Ambient.Value = scope;

        return scope;
    }

    /// <summary>
    /// The operation's key, when one has begun.
    /// </summary>
    public static string? Current => Active?.Key;

    private static Scope? Active => Scope.LiveFrom(Ambient.Value);

    /// <summary>
    /// The operation key a message handler's work runs under: the handler's name and the envelope's identifier, so a
    /// redelivery repeats it and two handlers of one envelope don't share it.
    /// </summary>
    /// <param name="handler">The handler's stable name, such as its type's full name.</param>
    /// <param name="envelope">The envelope's identifier.</param>
    /// <returns>The key; a long name is shortened to its hash.</returns>
    /// <exception cref="ArgumentException"><paramref name="handler"/> is null, empty or white space, or holds a colon.</exception>
    /// <remarks>
    /// The key is only as unique as the envelope's identifier. Take it from the message broker's envelope, minted where
    /// the message was published, never from the message's payload or a request's headers. A message published again
    /// under an identifier already used repeats the operation's keys and derived identifiers, so its writes are
    /// answered from the first message's.
    /// </remarks>
    public static string ForMessage(string handler, Guid envelope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handler);

        return Compose(MessageKind, handler, nameof(handler), envelope.ToString());
    }

    /// <summary>
    /// The operation key a message handler's work runs under, for a message whose identifier is a string, as a message
    /// broker's is: the handler's name and the message's identifier, so a redelivery repeats it and two handlers of one
    /// message don't share it.
    /// </summary>
    /// <param name="handler">The handler's stable name, such as its type's full name.</param>
    /// <param name="messageId">
    /// The message's identifier: 1 to <see cref="MaximumMessageIdLength"/> ASCII characters, not blank, as a message
    /// broker allows. One that isn't visible ASCII (a space, a control character) is shortened to its hash. A GUID's
    /// identifier gives the key <see cref="ForMessage(string, Guid)"/> gives for that GUID.
    /// </param>
    /// <returns>The key; a long name is shortened to its hash.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="handler"/> is null, empty or white space or holds a colon, or <paramref name="messageId"/> is blank, too long, or
    /// not ASCII.
    /// </exception>
    /// <remarks>
    /// The key is only as unique as the message's identifier. Take it from the message broker's envelope, chosen where
    /// the message was sent, never from the message's payload or a request's headers. A message that arrives with no
    /// identifier, and is given a new one on each delivery, gets no deduplication across its redeliveries.
    /// </remarks>
    public static string ForMessage(string handler, string messageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handler);

        if (string.IsNullOrWhiteSpace(messageId) || messageId.Length > MaximumMessageIdLength || !Ascii.IsValid(messageId))
        {
            throw new ArgumentException(
                $"A message's identifier is 1 to {MaximumMessageIdLength} ASCII characters, not blank.", nameof(messageId));
        }

        var occurrence = IdempotencyKeys.IsValid(messageId)
            ? messageId
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(messageId)));

        return Compose(MessageKind, handler, nameof(handler), occurrence);
    }

    /// <summary>
    /// The longest message identifier <see cref="ForMessage(string, string)"/> accepts: a message broker's limit, and
    /// short enough that the key, with a shortened name, is never over <see cref="MaximumKeyLength"/>.
    /// </summary>
    public const int MaximumMessageIdLength = 128;

    /// <summary>
    /// The operation key a recurring job's run works under: the job's name and the occurrence's due time, so the same
    /// occurrence run again (a lease that lapsed, a service stopped mid-run) repeats it, and the next occurrence doesn't.
    /// </summary>
    /// <param name="job">The job's name.</param>
    /// <param name="dueAt">When the occurrence was due.</param>
    /// <returns>The key; a long name is shortened to its hash.</returns>
    /// <exception cref="ArgumentException"><paramref name="job"/> is null, empty or white space, or holds a colon.</exception>
    /// <remarks>
    /// Take the due time from the scheduler that owns the job's occurrences, never from a payload or a request's
    /// headers. Two runs given the same job and due time repeat each other's keys and derived identifiers, so the
    /// second's writes are answered from the first's.
    /// </remarks>
    public static string ForJob(string job, DateTimeOffset dueAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(job);

        return Compose(JobKind, job, nameof(job), dueAt.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The key for one write inside the current operation, or null outside one.
    /// </summary>
    /// <param name="method">The request's method.</param>
    /// <param name="address">The request's address.</param>
    /// <param name="body">The request's body; empty when it has none.</param>
    /// <param name="context">
    /// The context headers the request carries, by name and value, such as <c>Bitwarden-User</c>: whom the write is
    /// for. Empty when it carries none.
    /// </param>
    /// <returns>64 lower-case hex characters, or null when no operation has begun.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="method"/>, <paramref name="address"/> or a context value is not valid UTF-16 text (a lone
    /// surrogate), or a context header's name is blank, holds a colon, or is given twice.
    /// </exception>
    /// <remarks>
    /// The key is <see cref="CacheKey.DeriveCacheKey(CacheSecret, ReadOnlySpan{string})"/> with the operation's secret
    /// over <c>idempotency-key</c>, the operation's key, the method, the address, the body's SHA-256 in lower-case hex, the ordinal in
    /// decimal (how many identical writes the operation has made before), and each context header as
    /// <c>{name}:{value}</c>, its name in lower case, in order of name. Only a holder of the secret can compute it.
    /// Writes for different users or organizations get different keys whatever order they are made in.
    /// </remarks>
    public static string? KeyForWrite(
        string method, string address, ReadOnlySpan<byte> body, IEnumerable<KeyValuePair<string, string>> context)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(context);

        var headers = ContextParameters(context);

        if (Active is not { } scope)
        {
            return null;
        }

        const string invalidText = "The method, the address or a context value is not valid UTF-16 text.";
        string[] write = [scope.Key, method, address, Convert.ToHexStringLower(SHA256.HashData(body))];

        // Counted by everything but the ordinal, derived, so the scope holds no body and no context value.
        var ordinal = scope.Next(Derive(scope.Secret, invalidText, nameof(address), [WriteKeyPurpose, .. write, .. headers]));

        var key = Derive(
            scope.Secret,
            invalidText,
            nameof(address),
            [WriteKeyPurpose, .. write, ordinal.ToString(CultureInfo.InvariantCulture), .. headers]);

        return Convert.ToHexStringLower(key.AsSpan());
    }

    // Each header as one parameter, {name}:{value}, in order of name. A header's name has no colon, so the first one
    // ends it.
    private static string[] ContextParameters(IEnumerable<KeyValuePair<string, string>> context)
    {
        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in context)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains(':', StringComparison.Ordinal))
            {
                throw new ArgumentException("A context header's name is blank or holds a colon.", nameof(context));
            }

            ArgumentNullException.ThrowIfNull(value, nameof(context));
            if (!headers.TryAdd(name.ToLowerInvariant(), value))
            {
                throw new ArgumentException("A context header is given twice.", nameof(context));
            }
        }

        return [.. headers.Select(header => $"{header.Key}:{header.Value}")];
    }

    /// <summary>
    /// An identifier, derived deterministically from the operation, for something the operation creates or sends, or
    /// null outside one: the same on every run of the operation, so a run again names the same thing.
    /// </summary>
    /// <param name="purpose">Tells this identifier from the operation's others, such as a template's name.</param>
    /// <param name="subject">
    /// Whom the thing is for, such as a user's or an organization's identifier, when the operation makes the same kind
    /// of thing for several. Null when it makes one.
    /// </param>
    /// <returns>A version 8 UUID (RFC 9562), or null when no operation has begun.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="purpose"/> or <paramref name="subject"/> is not valid UTF-16 text (a lone surrogate).
    /// </exception>
    /// <remarks>
    /// The purpose names what the identifier is for, and the subject whom. Never a person's data: no email address, no
    /// name. The identifier is the first 16 bytes of
    /// <see cref="CacheKey.DeriveCacheKey(CacheSecret, ReadOnlySpan{string})"/> with the operation's secret over
    /// <c>derived-id</c>, the operation's key, the purpose, the ordinal in decimal and the subject, if any, so only a
    /// holder of the secret can compute it. The same purpose and subject asked for twice in one run get two identifiers,
    /// in order. Pass the subject whenever one operation makes the same kind of thing for several users or
    /// organizations: each one's identifier then stays theirs whatever order a run asks in.
    /// </remarks>
    public static Guid? DeriveId(string purpose, string? subject = null)
    {
        ArgumentNullException.ThrowIfNull(purpose);

        if (Active is not { } scope)
        {
            return null;
        }

        const string invalidText = "The purpose or the subject is not valid UTF-16 text.";
        string[] whom = subject is null ? [] : [subject];

        var ordinal = scope.Next(Derive(scope.Secret, invalidText, nameof(purpose), [DerivedIdPurpose, scope.Key, purpose, .. whom]));
        var derived = Derive(
            scope.Secret,
            invalidText,
            nameof(purpose),
            [DerivedIdPurpose, scope.Key, purpose, ordinal.ToString(CultureInfo.InvariantCulture), .. whom]);
        var bytes = derived.AsSpan()[..16].ToArray();

        // RFC 9562 version 8 (custom) and the RFC variant, in the big-endian byte order the RFC writes.
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }

    // {kind}:{name}:{occurrence}. Neither the kind nor the name holds a colon, so only the occurrence can, and a key reads
    // back one way.
    private static string Compose(string kind, string name, string parameterName, string occurrence)
    {
        if (name.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("A handler's or a job's name may not hold a colon.", parameterName);
        }

        var key = $"{kind}:{name}:{occurrence}";
        if (key.Length <= MaximumKeyLength && IdempotencyKeys.IsValid(key))
        {
            return key;
        }

        return $"{kind}:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)))}:{occurrence}";
    }

    private static CacheKey Derive(CacheSecret secret, string invalidText, string parameterName, params ReadOnlySpan<string> parameters)
    {
        try
        {
            return CacheKey.DeriveCacheKey(secret, parameters);
        }
        catch (EncoderFallbackException)
        {
            // Not chained: the inner exception's message quotes the character, and the text may be an address.
            throw new ArgumentException(invalidText, parameterName);
        }
    }

    private sealed class Scope(string key, CacheSecret secret, Scope? outer) : IDisposable
    {
        private readonly ConcurrentDictionary<CacheKey, int> _counts = new();

        public string Key { get; } = key;

        public CacheSecret Secret { get; } = secret;

        public bool Ended { get; private set; }

        private Scope? Outer { get; } = outer;

        /// <summary>
        /// How many times the operation has asked for <paramref name="target"/> before.
        /// </summary>
        public int Next(CacheKey target) => _counts.AddOrUpdate(target, 0, (_, count) => count + 1);

        /// <summary>
        /// The nearest scope from <paramref name="scope"/> outwards that has not ended, or null.
        /// </summary>
        public static Scope? LiveFrom(Scope? scope)
        {
            while (scope is { Ended: true })
            {
                scope = scope.Outer;
            }

            return scope;
        }

        public void Dispose()
        {
            if (Ended)
            {
                return;
            }

            Ended = true;

            // Only the current scope hands back to its outer one: a scope ended out of order leaves alone a scope begun
            // since, which skips past it when it ends in turn.
            if (ReferenceEquals(Ambient.Value, this))
            {
                Ambient.Value = LiveFrom(Outer);
            }
        }
    }
}
