using System.Collections.Concurrent;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Index answers this process has read from a service's owned cache, held so the next read of the
/// same question confirms them in one round trip instead of reading them again in three.
/// </summary>
/// <remarks>
/// <para>
/// Not a cache of its own: nothing here is ever answered without asking Redis first. A held answer
/// is used only when Redis confirms, in one pipelined round trip, that it still holds exactly what
/// was read: the subject's generation, the index entry under it, and every resource it names at the
/// version read and not a tombstone (<see cref="CachedReads{TClient}"/>). So an owner's write
/// reaches every reader at once, as it does without this, and an answer is never older than the one
/// Redis would give.
/// </para>
/// <para>
/// It holds what the owned cache already holds, for the readers already allowed to read it, and
/// only while the process has a service cache. Bounded in size, and
/// each answer is let go after a short lifetime so memory follows what is asked now.
/// </para>
/// </remarks>
/// <param name="capacity">The most answers held; beyond it nothing new is held until some expire.</param>
/// <param name="lifetime">How long an answer is held after it was read.</param>
internal sealed class NearIndexAnswers(int capacity, TimeSpan lifetime)
{
    /// <summary>
    /// Switches holding answers off: <c>false</c>.
    /// </summary>
    public const string EnabledKey = "Caching:Owned:InProcess:Enabled";

    /// <summary>
    /// The most answers held.
    /// </summary>
    public const string CapacityKey = "Caching:Owned:InProcess:Capacity";

    /// <summary>
    /// How long an answer is held.
    /// </summary>
    public const string LifetimeKey = "Caching:Owned:InProcess:Lifetime";

    /// <summary>
    /// The default for <see cref="CapacityKey"/>.
    /// </summary>
    public const int DefaultCapacity = 10_000;

    /// <summary>
    /// The default for <see cref="LifetimeKey"/>.
    /// </summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, (Answer Answer, long Until)> _answers = new(StringComparer.Ordinal);

    /// <summary>
    /// The answer held for a question, if it has not outlived its lifetime.
    /// </summary>
    /// <param name="question">The index key without its generation.</param>
    /// <param name="answer">The answer.</param>
    /// <returns>Whether one is held.</returns>
    public bool TryGet(string question, out Answer answer)
    {
        if (_answers.TryGetValue(question, out var held))
        {
            if (System.Environment.TickCount64 < held.Until)
            {
                answer = held.Answer;
                return true;
            }

            _answers.TryRemove(KeyValuePair.Create(question, held));
        }

        answer = null!;
        return false;
    }

    /// <summary>
    /// Holds an answer just read from Redis.
    /// </summary>
    /// <param name="question">The index key without its generation.</param>
    /// <param name="answer">The answer.</param>
    public void Set(string question, Answer answer)
    {
        if (capacity <= 0 || lifetime <= TimeSpan.Zero)
        {
            return;
        }

        var now = System.Environment.TickCount64;

        if (_answers.Count >= capacity && !_answers.ContainsKey(question))
        {
            foreach (var expired in _answers.Where(held => held.Value.Until <= now))
            {
                _answers.TryRemove(expired);
            }

            if (_answers.Count >= capacity)
            {
                return;
            }
        }

        _answers[question] = (answer, now + (long)lifetime.TotalMilliseconds);
    }

    /// <summary>
    /// Lets go of an answer Redis no longer confirms.
    /// </summary>
    /// <param name="question">The index key without its generation.</param>
    public void Remove(string question) => _answers.TryRemove(question, out _);

    /// <summary>
    /// How many answers are held.
    /// </summary>
    public int Count => _answers.Count;

    /// <summary>
    /// One resource of an answer, as read.
    /// </summary>
    /// <param name="Key">The resource's entry key.</param>
    /// <param name="Version">Its version when read.</param>
    /// <param name="Data">The resource object, every cached attribute.</param>
    public sealed record Entry(string Key, string Version, string Data);

    /// <summary>
    /// An answer, as read under one generation.
    /// </summary>
    /// <param name="Generation">The subject's generation it was read under.</param>
    /// <param name="IndexKey">The index entry it was read from.</param>
    /// <param name="Entries">Each resource it names, in the index's order.</param>
    /// <param name="Carried">The facts the index carries, as JSON, or null.</param>
    public sealed record Answer(string Generation, string IndexKey, IReadOnlyList<Entry> Entries, string? Carried);
}
