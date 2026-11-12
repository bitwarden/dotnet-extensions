using System.Text.Json;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Resources.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// The cache reads for one client, as declared by the service it calls.
/// </summary>
/// <param name="service">The service called, as configured.</param>
/// <param name="reads">The owner's cacheable reads.</param>
/// <param name="connection">The service cache, or null when there is none.</param>
/// <param name="options">The client's options, for the scopes its credential holds.</param>
/// <param name="scopes">A scope per read, for the context it is made in.</param>
/// <param name="configuration">Switches one resource's reads off without a release.</param>
/// <param name="logger">Records a cache that could not be read; never what it held.</param>
/// <param name="indexes">The owner's indexed questions.</param>
/// <param name="answers">The owner's answers.</param>
internal sealed class CachedReads<TClient>(
    string service,
    IReadOnlyDictionary<string, CacheableRead> reads,
    IReadOnlyDictionary<string, CacheableIndexRead> indexes,
    CachedReadsConnection? connection,
    IOptionsMonitor<BitwardenClientOptions> options,
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<CachedReads<TClient>> logger,
    IReadOnlyDictionary<string, CacheableAnswerRead>? answers = null) : ICachedReads<TClient>
{
    /// <summary>
    /// How long a read may wait on Redis, on the connection's clock, before the service is asked instead.
    /// </summary>
    internal static readonly TimeSpan Patience = TimeSpan.FromMilliseconds(250);

    private readonly byte[] _answerSecret = OwnedAnswerKey.ReadSecret(configuration);

    private readonly NearIndexAnswers _near = new(
        int.TryParse(configuration[NearIndexAnswers.CapacityKey], out var capacity) ? capacity : NearIndexAnswers.DefaultCapacity,
        TimeSpan.TryParse(configuration[NearIndexAnswers.LifetimeKey], System.Globalization.CultureInfo.InvariantCulture, out var lifetime)
            ? lifetime
            : NearIndexAnswers.DefaultLifetime);

    public async Task<CachedAnswer> ReadAsync(
        string operationId,
        Guid key,
        IReadOnlyCollection<string> fields,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (connection is null
            || !reads.TryGetValue(operationId, out var read)
            || string.Equals(configuration[$"{OwnedCacheEntry.ResourcesSection}:{Owner(read)}:{read.Resource}:Enabled"], "false", StringComparison.OrdinalIgnoreCase)
            || !options.Get(service).Scopes.Contains(read.Scope, StringComparer.Ordinal)
            || fields.Count == 0
            || fields.Any(field => !read.Fields.Contains(field)))
        {
            return CachedAnswer.Miss;
        }

        (Guid? Organization, Guid? User, Guid? Provider) call;
        var stated = OutgoingCall.Peek();

        using (var scope = scopes.CreateScope())
        {
            call = OutgoingContext.Of(scope.ServiceProvider, stated);
        }

        if (!Satisfies(read.Organization, call.Organization) || !Satisfies(read.User, call.User) || !Satisfies(read.Provider, call.Provider))
        {
            return CachedAnswer.Miss;
        }

        var entry = await LookUpAsync(OwnedCacheEntry.Key(read.Key, key), cancellationToken);

        if (entry is null
            || entry.ContainsKey(OwnedCacheEntry.Gone)
            || !OwnedBy(read, entry, key, call))
        {
            return CachedAnswer.Miss;
        }

        CachedAnswer answer;

        if (entry.ContainsKey(OwnedCacheEntry.None))
        {
            answer = CachedAnswer.NothingRecorded;
        }
        else if (entry.TryGetValue(OwnedCacheEntry.Data, out var data) && Shaped(data, fields) is { } resource)
        {
            answer = CachedAnswer.Hit(resource);
        }
        else
        {
            return CachedAnswer.Miss;
        }

        // Answered: the statement is used up, as the call would have used it.
        if (stated is not null)
        {
            OutgoingCall.Take();
        }

        return answer;
    }

    public async Task<IReadOnlyList<string>?> ReadIndexAsync(
        string operationId,
        string question,
        Guid subject,
        IReadOnlyCollection<string> fields,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (connection is null
            || !indexes.TryGetValue($"{operationId}/{question}", out var index)
            || reads.Values.FirstOrDefault(read => read.Resource == index.Resource) is not { } resource
            || string.Equals(configuration[$"{OwnedCacheEntry.ResourcesSection}:{Owner(resource)}:{resource.Resource}:Enabled"], "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(configuration[$"{OwnedCacheEntry.ResourcesSection}:{Owner(resource)}:{index.Name}:Enabled"], "false", StringComparison.OrdinalIgnoreCase)
            || !options.Get(service).Scopes.Contains(index.Scope, StringComparer.Ordinal)
            || fields.Count == 0
            || fields.Any(field => !resource.Fields.Contains(field) && !index.Carries.Contains(field)))
        {
            return null;
        }

        (Guid? Organization, Guid? User, Guid? Provider) call;
        var stated = OutgoingCall.Peek();

        using (var scope = scopes.CreateScope())
        {
            call = OutgoingContext.Of(scope.ServiceProvider, stated);
        }

        // A principal of the call must be the one asked about; a subject the caller names is
        // checked by the owner the call names instead.
        if ((index.SubjectAxis is { } axis && Axis(axis, call) != subject)
            || (index.Within is { } within && Axis(within, call) is null)
            || !Satisfies(index.Organization, call.Organization)
            || !Satisfies(index.User, call.User)
            || !Satisfies(index.Provider, call.Provider))
        {
            return null;
        }

        var owner = index.Within is { } inside ? Axis(inside, call) : null;
        var answer = await AnswerAsync(index, resource, owner, subject, fields, cancellationToken);

        if (answer is not null && stated is not null)
        {
            OutgoingCall.Take();
        }

        return answer;
    }

    public async Task<CachedAnswer> ReadAnswerAsync(
        string operationId,
        string answer,
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (connection is null
            || answers is null
            || !answers.TryGetValue($"{operationId}/{answer}", out var published)
            || string.Equals(configuration[$"{OwnedCacheEntry.ResourcesSection}:{published.Owner}:{published.Name}:Enabled"], "false", StringComparison.OrdinalIgnoreCase)
            || !options.Get(service).Scopes.Contains(published.Scope, StringComparer.Ordinal))
        {
            return CachedAnswer.Miss;
        }

        Guid subject;
        if (published.Digested)
        {
            subject = OwnedAnswerKey.Of(published.Name, key, _answerSecret);
        }
        else if (!Guid.TryParse(key, out subject))
        {
            return CachedAnswer.Miss;
        }

        (Guid? Organization, Guid? User, Guid? Provider) call;
        var stated = OutgoingCall.Peek();

        using (var scope = scopes.CreateScope())
        {
            call = OutgoingContext.Of(scope.ServiceProvider, stated);
        }

        if (!Satisfies(published.Organization, call.Organization) || !Satisfies(published.User, call.User)
            || !Satisfies(published.Provider, call.Provider))
        {
            return CachedAnswer.Miss;
        }

        var entry = await LookUpAsync(OwnedCacheEntry.Key(published.Key, subject), cancellationToken);

        CachedAnswer found;

        if (entry is null || entry.ContainsKey(OwnedCacheEntry.Gone))
        {
            return CachedAnswer.Miss;
        }
        else if (entry.ContainsKey(OwnedCacheEntry.None))
        {
            found = CachedAnswer.NothingRecorded;
        }
        else if (entry.TryGetValue(OwnedCacheEntry.Data, out var data))
        {
            found = CachedAnswer.Hit(data);
        }
        else
        {
            return CachedAnswer.Miss;
        }

        if (stated is not null)
        {
            OutgoingCall.Take();
        }

        return found;
    }

    private static Guid? Axis(string axis, (Guid? Organization, Guid? User, Guid? Provider) call) => axis switch
    {
        "organization" => call.Organization,
        "user" => call.User,
        "provider" => call.Provider,
        _ => null,
    };

    private async Task<IReadOnlyList<string>?> AnswerAsync(
        CacheableIndexRead index,
        CacheableRead resource,
        Guid? owner,
        Guid subject,
        IReadOnlyCollection<string> fields,
        CancellationToken cancellationToken)
    {
        try
        {
            var database = await connection!.DatabaseAsync().WaitAsync(Patience, connection!.Time, cancellationToken);
            var who = subject.ToString("D");
            var generationKey = index.Generation.Replace("{subject}", who, StringComparison.Ordinal);
            var keyOf = index.Key
                .Replace("{owner}", owner?.ToString("D") ?? string.Empty, StringComparison.Ordinal)
                .Replace("{subject}", who, StringComparison.Ordinal);

            if (database is null)
            {
                return null;
            }

            var held = NearEnabled ? await ConfirmedAsync(database, keyOf, generationKey, cancellationToken) : default;
            var near = held.Answer;

            if (near is null)
            {
                var generation = held.Generation is { HasValue: true } known
                    ? known
                    : await database.HashGetAsync(generationKey, "g").WaitAsync(Patience, connection!.Time, cancellationToken);
                var indexKey = keyOf.Replace("{generation}", generation.ToString(), StringComparison.Ordinal);

                if (!generation.HasValue
                    || await database.StringGetAsync(indexKey).WaitAsync(Patience, connection!.Time, cancellationToken) is not { HasValue: true } listed
                    || JsonNode.Parse(listed.ToString()) is not JsonObject indexed
                    || indexed["ids"] is not JsonArray listedIds)
                {
                    return null;
                }

                var keys = listedIds
                    .Select(id => Guid.TryParse(id?.GetValue<string>(), out var key) ? OwnedCacheEntry.Key(resource.Key, key) : null)
                    .ToArray();

                if (keys.Any(key => key is null))
                {
                    return null;
                }

                var read = await Task.WhenAll(keys.Select(key => database.HashGetAllAsync(key!)))
                    .WaitAsync(Patience, connection!.Time, cancellationToken);
                var entries = new List<NearIndexAnswers.Entry>(read.Length);

                for (var i = 0; i < read.Length; i++)
                {
                    var entry = read[i].ToDictionary(field => field.Name.ToString(), field => field.Value.ToString(), StringComparer.Ordinal);

                    if (entry.ContainsKey(OwnedCacheEntry.Gone)
                        || !entry.TryGetValue(OwnedCacheEntry.Version, out var version)
                        || !entry.TryGetValue(OwnedCacheEntry.Data, out var data))
                    {
                        return null;
                    }

                    entries.Add(new NearIndexAnswers.Entry(keys[i]!, version, data));
                }

                near = new NearIndexAnswers.Answer(
                    generation.ToString(), indexKey, entries, (indexed["carried"] as JsonObject)?.ToJsonString());
            }

            var carried = near.Carried is { } facts ? JsonNode.Parse(facts) as JsonObject : null;
            var answer = new List<string>(near.Entries.Count);

            foreach (var entry in near.Entries)
            {
                if (!Answers(index, entry.Data, who)
                    || Carrying(index, entry.Data, carried, fields) is not { } complete
                    || Shaped(complete, fields) is not { } shaped)
                {
                    return null;
                }

                answer.Add(shaped);
            }

            if (NearEnabled && held.Answer is null)
            {
                _near.Set(keyOf, near);
            }

            return answer;
        }
        // An entry of the wrong shape (JsonException, or InvalidOperationException from a value of the wrong kind) is
        // the owner's data being unreadable, not Redis failing.
        catch (Exception exception) when (exception is RedisException or TimeoutException or ObjectDisposedException
            or JsonException or InvalidOperationException)
        {
            if (exception is not (JsonException or InvalidOperationException))
            {
                connection!.Failed();
            }

            logger.LogDebug(exception, "Could not read the service cache index for {Service}; asking it instead.", service);
            return null;
        }
    }

    /// <summary>
    /// Whether index answers are held in this process between reads (on unless switched off).
    /// </summary>
    private bool NearEnabled =>
        !string.Equals(configuration[NearIndexAnswers.EnabledKey], "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The answer this process holds for the question, if Redis still holds it unchanged: the
    /// subject's generation is the one it was read under, the index entry is still there, and every
    /// resource it names is at the version read and not a tombstone. One round trip, pipelined.
    /// </summary>
    /// <returns>
    /// The held answer when confirmed, else null; and the generation as read, which a miss reuses.
    /// </returns>
    private async Task<(NearIndexAnswers.Answer? Answer, RedisValue? Generation)> ConfirmedAsync(
        IDatabase database,
        string keyOf,
        string generationKey,
        CancellationToken cancellationToken)
    {
        if (!_near.TryGet(keyOf, out var held))
        {
            return default;
        }

        var batch = database.CreateBatch();
        var generation = batch.HashGetAsync(generationKey, "g");
        var listed = batch.KeyExistsAsync(held.IndexKey);
        var versions = held.Entries
            .Select(entry => batch.HashGetAsync(entry.Key, [OwnedCacheEntry.Version, OwnedCacheEntry.Gone]))
            .ToArray();
        batch.Execute();

        await Task.WhenAll(versions.Prepend<Task>(listed).Prepend(generation)).WaitAsync(Patience, connection!.Time, cancellationToken);

        var unchanged = generation.Result.HasValue
            && generation.Result.ToString() == held.Generation
            && listed.Result
            && versions.Select((version, i) =>
                    version.Result[1].IsNull && version.Result[0].ToString() == held.Entries[i].Version)
                .All(same => same);

        if (unchanged)
        {
            return (held, generation.Result);
        }

        _near.Remove(keyOf);
        return (null, generation.Result);
    }

    /// <summary>
    /// The cached resource with the facts the answer carries that were asked for; null if one is missing.
    /// </summary>
    private static string? Carrying(CacheableIndexRead index, string data, JsonObject? carried, IReadOnlyCollection<string> fields)
    {
        var wanted = fields.Where(index.Carries.Contains).ToList();

        if (wanted.Count == 0)
        {
            return data;
        }

        if (carried is null || wanted.Any(field => !carried.ContainsKey(field))
            || JsonNode.Parse(data) is not JsonObject resource || resource["attributes"] is not JsonObject attributes)
        {
            return null;
        }

        foreach (var field in wanted)
        {
            attributes[field] = carried[field]?.DeepClone();
        }

        return resource.ToJsonString();
    }

    /// <summary>
    /// Whether a cached resource still answers the question: its subject, and the question's values.
    /// </summary>
    private static bool Answers(CacheableIndexRead index, string data, string subject)
    {
        if (JsonNode.Parse(data)?["attributes"] is not JsonObject attributes
            || (index.SubjectAttribute is { } held
                && !string.Equals(Value(attributes[held]), subject, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return index.Where.All(condition => Value(attributes[condition.Key]) is { } value && condition.Value.Contains(value));
    }

    /// <summary>
    /// An attribute's value as the contract writes it: a string as itself, a boolean in lower case.
    /// </summary>
    private static string? Value(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonValue value when value.TryGetValue<bool>(out var flag) => flag ? "true" : "false",
        JsonValue value => value.ToJsonString(),
        _ => null,
    };

    private async Task<Dictionary<string, string>?> LookUpAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var database = await connection!.DatabaseAsync().WaitAsync(Patience, connection!.Time, cancellationToken);

            if (database is null)
            {
                return null;
            }

            var fields = await database.HashGetAllAsync(key).WaitAsync(Patience, connection!.Time, cancellationToken);

            return fields.Length == 0
                ? null
                : fields.ToDictionary(field => field.Name.ToString(), field => field.Value.ToString(), StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException or ObjectDisposedException)
        {
            connection!.Failed();
            logger.LogDebug(exception, "Could not read the service cache for {Service}; asking it instead.", service);
            return null;
        }
    }

    /// <summary>
    /// The entry belongs to what the call names: the owner the service's row-level security would
    /// compare. A row that is its own owner may be read by a call that names none of its kind. A
    /// resource read across owners answers whatever the call names, as the service does, once the
    /// entry carries a well-formed owner.
    /// </summary>
    private static bool OwnedBy(
        CacheableRead read,
        Dictionary<string, string> entry,
        Guid key,
        (Guid? Organization, Guid? User, Guid? Provider) call)
    {
        if (read.OwnerAxis is null)
        {
            return true;
        }

        if (!entry.TryGetValue(OwnedCacheEntry.Owner, out var stored)
            || OwnerOf(stored, read.OwnerAxis) is not { } owner
            || !Guid.TryParse(owner, out var entryOwner))
        {
            return false;
        }

        if (read.AcrossOwners)
        {
            return true;
        }

        var named = read.OwnerAxis switch
        {
            "organization" => call.Organization,
            "user" => call.User,
            "provider" => call.Provider,
            _ => null,
        };

        return read.OwnerIsSelf
            ? entryOwner == key && (named is null || named == entryOwner)
            : named == entryOwner;
    }

    /// <summary>
    /// The owner an entry's owner field names on one axis; null when the field is unreadable or names none, so an
    /// entry of the wrong shape is a miss rather than a failed call.
    /// </summary>
    private static string? OwnerOf(string stored, string axis)
    {
        try
        {
            return JsonNode.Parse(stored) is JsonObject owners
                && owners[axis] is JsonValue value
                && value.TryGetValue<string>(out var owner)
                ? owner
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Satisfies(string requirement, Guid? present) => requirement switch
    {
        "required" => present is not null,
        "forbidden" => present is null,
        _ => true,
    };

    /// <summary>
    /// The cached resource with only the attributes asked for, as a sparse fieldset would shape it.
    /// </summary>
    private static string? Shaped(string data, IReadOnlyCollection<string> fields)
    {
        try
        {
            if (JsonNode.Parse(data) is not JsonObject resource)
            {
                return null;
            }

            if (resource["attributes"] is JsonObject attributes)
            {
                foreach (var name in attributes.Select(attribute => attribute.Key).ToList())
                {
                    if (!fields.Contains(name))
                    {
                        attributes.Remove(name);
                    }
                }
            }

            return resource.ToJsonString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The owner's name, from its key template's prefix.
    /// </summary>
    private static string Owner(CacheableRead read) => read.Key[..Math.Max(0, read.Key.IndexOf(':', StringComparison.Ordinal))];
}
