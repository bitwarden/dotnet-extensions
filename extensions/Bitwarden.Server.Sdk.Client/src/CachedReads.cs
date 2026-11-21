using System.Text.Json;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Context;
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
/// <param name="metrics">Counts where each read's entry came from while a request memo is current.</param>
internal sealed class CachedReads<TClient>(
    string service,
    IReadOnlyDictionary<string, CacheableRead> reads,
    CachedReadsConnection? connection,
    IOptionsMonitor<BitwardenClientOptions> options,
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<CachedReads<TClient>> logger,
    RequestMemoMetrics metrics) : ICachedReads<TClient>
{
    /// <summary>
    /// How long a read may wait on Redis, on the connection's clock, before the service is asked instead.
    /// </summary>
    internal static readonly TimeSpan Patience = TimeSpan.FromMilliseconds(250);

    public Task<CachedAnswer> ReadAsync(
        string operationId,
        Guid key,
        IReadOnlyCollection<string> fields,
        CancellationToken cancellationToken = default) =>
        ReadStatedAsync(operationId, key, fields, cancellationToken);

    public Task<CachedAnswer> ReadCurrentAsync(
        string operationId,
        IReadOnlyCollection<string> fields,
        CancellationToken cancellationToken = default) =>
        ReadStatedAsync(operationId, null, fields, cancellationToken);

    public Task<CachedAnswer> ReadManyAsync(
        string operationId,
        int pageNumber,
        int pageSize,
        IReadOnlyCollection<string> fields,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        return ReadStatedAsync(operationId, null, fields, cancellationToken, new CachedPage(pageNumber, pageSize));
    }

    public Task<CachedAnswer> ReadAllAsync(
        string operationId,
        IReadOnlyCollection<string> fields,
        CancellationToken cancellationToken = default) =>
        reads.TryGetValue(operationId, out var read) && read.AtMost is { } atMost
            ? ReadManyAsync(operationId, 1, atMost, fields, cancellationToken)
            : Task.FromResult(CachedAnswer.Miss);

    public OutgoingCallContext NextCall()
    {
        using var scope = scopes.CreateScope();
        var stated = OutgoingCall.Peek();
        var (organization, user, provider) = OutgoingContext.Of(scope.ServiceProvider, stated);

        // Asserted exactly where the call's context token would be exchanged from this service's own token.
        return new OutgoingCallContext(organization, user, provider, ContextHandler.SubjectToken(scope.ServiceProvider, stated) is null);
    }

    public Task<CachedAnswer> ReadAsync(
        string operationId,
        Guid? key,
        IReadOnlyCollection<string> fields,
        OutgoingCallContext call,
        CancellationToken cancellationToken = default) =>
        ReadCoreAsync(operationId, key, fields, call, cancellationToken);

    public void Answered()
    {
        if (OutgoingCall.Peek() is not null)
        {
            OutgoingCall.Take();
        }
    }

    private async Task<CachedAnswer> ReadStatedAsync(
        string operationId,
        Guid? key,
        IReadOnlyCollection<string> fields,
        CancellationToken cancellationToken,
        CachedPage? page = null)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (connection is null || !reads.ContainsKey(operationId))
        {
            return CachedAnswer.Miss;
        }

        var stated = OutgoingCall.Peek();
        var answer = await ReadCoreAsync(operationId, key, fields, NextCall(), cancellationToken, page);

        // Answered: the statement is used up, as the call would have used it.
        if (answer.Kind != CachedAnswerKind.Miss && stated is not null)
        {
            OutgoingCall.Take();
        }

        return answer;
    }

    private async Task<CachedAnswer> ReadCoreAsync(
        string operationId,
        Guid? key,
        IReadOnlyCollection<string> fields,
        OutgoingCallContext call,
        CancellationToken cancellationToken,
        CachedPage? page = null)
    {
        ArgumentNullException.ThrowIfNull(fields);

        // A read-many is read only a page at a time, and a read of one never so.
        if (connection is null
            || !reads.TryGetValue(operationId, out var read)
            || read.ReadMany != (page is not null)
            || string.Equals(configuration[$"{OwnedCacheEntry.ResourcesSection}:{Owner(read)}:{read.Resource}:Enabled"], "false", StringComparison.OrdinalIgnoreCase)
            || !options.Get(service).Scopes.Contains(read.Scope, StringComparer.Ordinal)
            || read.Format != OwnedCacheEntry.CurrentFormat
            || (fields.Count == 0 && read.PerCaller is { Count: > 0 })
            || fields.Any(field => !read.Fields.Contains(field)))
        {
            // No fieldset asks for every attribute: a hit only where none is worked out per caller. A fieldset hits only
            // where every attribute it names is one the entry holds; a name the service doesn't know is its to refuse.
            return CachedAnswer.Miss;
        }

        if (!Satisfies(read.Organization, call.Organization) || !Satisfies(read.User, call.User) || !Satisfies(read.Provider, call.Provider)
            || !AdmitsAssertion(read.AssertedBy, call))
        {
            return CachedAnswer.Miss;
        }

        // The key and the parties the template names, from the request and the call; a placeholder left unfilled (a
        // read by key asked as current, or the reverse) is a miss.
        if (OwnedCacheEntry.Key(read.Key, key, call.Organization, call.User, call.Provider) is not { } entryKey)
        {
            return CachedAnswer.Miss;
        }

        var entry = await LookUpAsync(entryKey, cancellationToken);

        if (entry is null
            || entry.ContainsKey(OwnedCacheEntry.Gone)
            || !entry.TryGetValue(OwnedCacheEntry.Format, out var format)
            || format != OwnedCacheEntry.CurrentFormat.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || !OwnedBy(read, entry, key, call))
        {
            return CachedAnswer.Miss;
        }

        if (entry.ContainsKey(OwnedCacheEntry.None))
        {
            return CachedAnswer.NothingRecorded;
        }

        if (!entry.TryGetValue(OwnedCacheEntry.Data, out var data))
        {
            return CachedAnswer.Miss;
        }

        if (page is { } asked)
        {
            return Paged(data, fields, asked) is ({ } resources, var total) ? CachedAnswer.HitPage(resources, total) : CachedAnswer.Miss;
        }

        return Shaped(data, fields) is { } resource ? CachedAnswer.Hit(resource) : CachedAnswer.Miss;
    }

    /// <summary>
    /// The entry under <paramref name="key"/>: what the request already read of it, while a request memo is current
    /// (<see cref="RequestMemo"/>), so a repeated read waits on Redis no more than the repeated call it stands for waits on
    /// the service. Its absence is remembered too; Redis failing is not.
    /// </summary>
    /// <remarks>
    /// The entry is the same whoever reads it: every check of whether it answers this call is made on it afresh.
    /// </remarks>
    private async Task<IReadOnlyDictionary<string, string>?> LookUpAsync(string key, CancellationToken cancellationToken)
    {
        if (RequestMemo.Current is not { } memo || RequestMemoHandler.Switched(configuration))
        {
            return (await ReadEntryAsync(key, cancellationToken)).Entry;
        }

        var (entry, outcome) = await memo.RememberAsync(
            $"cache {service} {key}",
            OutgoingCall.IsFresh,
            async token =>
            {
                var (read, failed) = await ReadEntryAsync(key, token);
                return new RequestMemoRead<IReadOnlyDictionary<string, string>?>(
                    read,
                    read?.Sum(field => field.Key.Length + field.Value.Length) ?? 0,
                    failed ? RequestMemoKeep.Share : RequestMemoKeep.Keep);
            },
            cancellationToken);

        metrics.Counted(service, RequestMemoMetrics.CacheSource, outcome);

        return entry;
    }

    private async Task<(IReadOnlyDictionary<string, string>? Entry, bool Failed)> ReadEntryAsync(
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            var database = await connection!.DatabaseAsync().WaitAsync(Patience, connection!.Time, cancellationToken);

            if (database is null)
            {
                return (null, true);
            }

            var fields = await database.HashGetAllAsync(key).WaitAsync(Patience, connection!.Time, cancellationToken);

            return fields.Length == 0
                ? (null, false)
                : (fields.ToDictionary(field => field.Name.ToString(), field => field.Value.ToString(), StringComparer.Ordinal), false);
        }
        // A connection string StackExchange.Redis can't parse throws ArgumentException on every read: a miss, as an
        // unreachable Redis is.
        catch (Exception exception) when (exception is RedisException or TimeoutException or ObjectDisposedException or ArgumentException)
        {
            connection!.Failed();
            logger.LogDebug(exception, "Could not read the service cache for {Service}; asking it instead.", service);
            return (null, true);
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
        IReadOnlyDictionary<string, string> entry,
        Guid? key,
        OutgoingCallContext call)
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
        // Not JSON, or an object naming a property twice, which JsonObject refuses with ArgumentException when read.
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the operation would admit the call's context: a person's own (delegated) context always, as one naming
    /// nobody; one this service asserts naming a person only where the operation names this service among those it
    /// admits it from (<c>assertedBy</c>), as the operation checks the caller's client (<c>internal.{name}</c>).
    /// </summary>
    private bool AdmitsAssertion(IReadOnlySet<string>? assertedBy, OutgoingCallContext call) =>
        !call.Asserted
        || call.User is null
        || (assertedBy?.Contains(options.Get(service).CallerName) ?? false);

    private static bool Satisfies(string requirement, Guid? present) => requirement switch
    {
        "required" => present is not null,
        "forbidden" => present is null,
        _ => true,
    };

    /// <summary>
    /// The cached resource with only the attributes asked for, as a sparse fieldset would shape it; the whole entry when
    /// no fieldset was asked for.
    /// </summary>
    private static string? Shaped(string data, IReadOnlyCollection<string> fields)
    {
        try
        {
            if (JsonNode.Parse(data) is not JsonObject resource)
            {
                return null;
            }

            Shape(resource, fields);
            return resource.ToJsonString();
        }
        // Not JSON, or an object naming a property twice, which JsonObject refuses with ArgumentException when read.
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The page asked for of a cached read-many's resources, each shaped as <see cref="Shaped"/> shapes one, and how many
    /// there are in all: a slice of the entry, in its order, which is the database's.
    /// </summary>
    private static (string? Resources, int Total) Paged(string data, IReadOnlyCollection<string> fields, CachedPage page)
    {
        try
        {
            if (JsonNode.Parse(data) is not JsonArray all)
            {
                return (null, 0);
            }

            var skip = (long)(page.Number - 1) * page.Size;
            var slice = new JsonArray();

            foreach (var element in all.Skip((int)Math.Min(skip, all.Count)).Take(page.Size))
            {
                if (element is not JsonObject resource)
                {
                    return (null, 0);
                }

                Shape(resource, fields);
                slice.Add(resource.DeepClone());
            }

            return (slice.ToJsonString(), all.Count);
        }
        // Not JSON, or an object naming a property twice, which JsonObject refuses with ArgumentException when read.
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return (null, 0);
        }
    }

    /// <summary>
    /// Removes from a resource every attribute not asked for; with no fieldset, keeps them all.
    /// </summary>
    private static void Shape(JsonObject resource, IReadOnlyCollection<string> fields)
    {
        if (fields.Count > 0 && resource["attributes"] is JsonObject attributes)
        {
            foreach (var name in attributes.Select(attribute => attribute.Key).ToList())
            {
                if (!fields.Contains(name))
                {
                    attributes.Remove(name);
                }
            }
        }
    }

    /// <summary>
    /// The owner's name, from its key template's prefix.
    /// </summary>
    private static string Owner(CacheableRead read) => read.Key[..Math.Max(0, read.Key.IndexOf(':', StringComparison.Ordinal))];
}
