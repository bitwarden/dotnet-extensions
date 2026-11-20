# Bitwarden.Server.Sdk.Resources.Caching

**Audience:** engineers writing a Bitwarden domain service whose resources other services read from Redis instead of
calling it.

## About

A Bitwarden service's own cache in Redis: the resources a domain service lets other services read from Redis instead
of calling it. A read becomes cacheable by extending `CacheableQuery<TEntity, TResource>`. The owning service is the
only writer: it fills an entry when it serves a read, invalidates it just before each change commits, and publishes in
its contract what a client may read and how to check it. Its clients read the entries through
Bitwarden.Server.Sdk.Client's `ICachedReads<TClient>`.

How an entry is stored and keyed is in
[Bitwarden.Server.Sdk.Resources.Caching.Abstractions](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources.Caching.Abstractions/src/PACKAGE.md),
which the owner and its clients share. This package depends on it, on Bitwarden.Server.Sdk.Data (the declared read,
row-level security, versions and erasure), Bitwarden.Server.Sdk.Resources, Bitwarden.Server.Sdk.Authorization,
Bitwarden.Server.Sdk.Context, Bitwarden.Server.Sdk.Errors and StackExchange.Redis. Through Bitwarden.Server.Sdk.Data
it uses Bitwarden.Server.Sdk.MessageBroker.Sender, only to discard a `cache.invalidate` message found in the outbox.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Resources.Caching
```

## Configuration

| Setting                                            | Default    | What it does                                                                                                                               |
| -------------------------------------------------- | ---------- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| `Caching:Owned:Redis:Configuration`                | None       | The service cache's Redis, for the owner and its clients alike. Unset: no cache. Names the service's own Redis user outside `Development`. |
| `Caching:Owned:Resources:{service}:{type}:Enabled` | `true`     | `false` stops filling (owner) and reading (clients) one resource, for an incident. Invalidation carries on.                                |
| `Caching:Owned:FillLimit`                          | `00:00:05` | How long after its database read began a fill is still written.                                                                            |
| `Caching:Owned:CommitAllowance`                    | `00:01:00` | How long after its invalidation a change has surely committed: how long an arrival's tombstone lives.                                      |

**Caching off** is the default: with no `Caching:Owned:Redis:Configuration` nothing is filled, recorded or
invalidated, clients ask the service every time, and every cacheable query reads the database. There is no memory-only
mode: the owner's memory is invisible to its clients.

**The Redis the cache needs.** Outside `Development`, a host with a cache (owner or client) refuses to start unless its
connection names a Redis user of its own (`user=...`, not `default`): entries carry no signature, and are trusted
because only the owner's user may write its prefix. Redis 7 or later is needed for those per-service users. The Redis
must evict nothing: `maxmemory-policy noeviction` (Redis's default), or no `maxmemory`. Where Redis may evict, the
owner reads that from `INFO memory`, logs an error, and fills nothing; every read is then answered by the service, and
invalidation carries on.

The host refuses to start unless `CommitAllowance` exceeds the database's command timeout plus `FillLimit`, and the
longest declared lifetime, which every tombstone but an arrival's lives, exceeds `FillLimit` plus `CommitAllowance`.

## Key concepts

- **The owner is the only writer.** A domain service fills and invalidates entries under its own prefix; other
  services only read them, and check the owner field themselves before using an entry.
- **An entry is the whole resource.** Every attribute, as the service returns it to any caller holding the read scope,
  with the row's owner, its version and the entry format, under `{service}:{type}:{id}`. Protected Data in it stays the
  ciphertext the database holds. What depends on the caller is named `.PerCaller(...)` and never cached.
- **One read path.** The query declares its read, and the read itself is the platform's and can't be overridden: the
  service's own entry when a client would answer from it, else the row through the context, so row-level security
  applies and a cached copy holds exactly what the service serves.
- **Invalidated just before commit.** A change tombstones the entries it makes stale as its transaction commits. A
  tombstone names the version the change made dead, so a slow read of the old row can't put it back.
- **What is cached is Data at Rest in Redis**, readable by every service granted the owner's prefix. A resource read
  across owners may cache no `[PersonalData]` attribute.

## Usage

### Make a read cacheable

A domain service makes a read cacheable by extending `CacheableQuery`: it declares the read, and the read itself is the
platform's.

`GetGroupQuery.cs`:

```csharp
using Bitwarden.Server.Sdk.Resources;
using Bitwarden.Server.Sdk.Resources.Caching;
using Bitwarden.Server.Sdk.Resources.Querying;
using Microsoft.EntityFrameworkCore;

public interface IGetGroupQuery
{
    Task<ResourceDocument<Group>> ReadAsync(string id, Fields<Group> fields = default, CancellationToken cancellationToken = default);
}

public sealed class GetGroupQuery(CacheableQueryServices services)
    : CacheableQuery<GroupEntity, Group>(services), IGetGroupQuery
{
    protected override void Configure(CacheableQueryBuilder<GroupEntity, Group> query) => query
        .Including(rows => rows.Include(group => group.Memberships))
        .Map(GroupMapping.ToResource)                  // from the row alone: no captured state
        .Lifetime(TimeSpan.FromMinutes(10))            // the backstop, not the mechanism
        .WhenCacheUnreachable(CacheWrites.Refuse);
}
```

It is registered as any query is, by its type, which turns caching on for its resource, and the service gives it its
cache. In `Program.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection.Extensions;

builder.Services.TryAddScoped<IGetGroupQuery, GetGroupQuery>();
builder.Services.AddOwnedCache<OrganizationDbContext>();
```

The handler takes it and calls it. `GroupsEndpoints.cs`:

```csharp
using Bitwarden.Server.Sdk.Resources;
using Bitwarden.Server.Sdk.Resources.Querying;

public static class GroupsEndpoints
{
    [StandardReadOne]
    public static Task<ResourceDocument<Group>> GetGroup(
        string id, IGetGroupQuery query, Fields<Group> fields = default, CancellationToken cancellationToken = default)
        => query.ReadAsync(id, fields, cancellationToken);
}
```

What the query gets:

- **One read path.** `ReadAsync` (by identifier), `ReadByKeyAsync` (by a key, `.KeyedBy`) and `ReadCurrentAsync` (by
  the party the context names, `.CurrentFrom`) belong to the base class. A read is answered from the service's own
  entry when a client would answer it from there: the entry exists, isn't a tombstone, is in this release's format, and
  its owner is the one the call names. Otherwise the row is found through the context, with what `.Including(...)`
  loads; `404` exactly as a hand-written read, or "nothing recorded" under a key; shaped by the map.
- **Write on read.** Each read from the database writes the resource, the row's owner, its version and the entry's
  format to its entry.
- **A slow read isn't kept.** A fill is skipped, and logged at debug, when more than `Caching:Owned:FillLimit` has
  passed since its database read began, measured on the reader's own clock.
- **A read inside a transaction isn't kept.** Under snapshot or repeatable-read isolation the row it read can be as
  old as the transaction, which the fill limit doesn't time. It is answered from the database and fills nothing.
- **Published.** Every operation whose handler takes a cacheable query carries `x-bitwarden-cache` in the contract:
  key template, scope, the context the operation requires, the owner to check, the attributes an entry holds
  (`fields`), those worked out per caller (`perCaller`), the entry format (`format`), and the services whose own
  assertion of a context naming a person the operation admits (`assertedBy`, from `[AcceptsAssertedContext]`; empty
  without it). Generated clients read their checks from it. A resource declared `.AcrossOwners(reason)` publishes
  `owner.acrossOwners`: its service answers any holder of the scope, so its clients check the scope alone.

### Work out attributes per caller

What depends on the caller, or on rows the resource's version doesn't cover, is named `.PerCaller(...)` and worked out
on every read by the query's `CompleteAsync`, never cached. It is published, so a client asking for any of it, or for
every attribute, asks the service. A read asking for none of it may be answered from the cache, by the owner or a
client.

`GetBookQuery.cs`:

```csharp
using Bitwarden.Server.Sdk.Resources.Caching;
using Bitwarden.Server.Sdk.Resources.Querying;

public sealed class GetBookQuery(CacheableQueryServices services) : CacheableQuery<BookEntity, Book>(services)
{
    protected override void Configure(CacheableQueryBuilder<BookEntity, Book> query) => query
        .Map(BookMapping.ToResource)
        .PerCaller("shelfmark");

    protected override ValueTask<Book> CompleteAsync(
        BookEntity entity, Book resource, Fields<Book> fields, CancellationToken cancellationToken) =>
        ValueTask.FromResult(fields.Includes("shelfmark") ? resource with { Shelfmark = entity.Shelfmark } : resource);
}
```

`CompleteAsync` runs only when the fieldset asks for at least one of them, after the row is read from the database. It
sets those attributes and nothing else: it can't refuse the read or hide the row.

### Read a table by more than one key

A table may back several cacheable queries, each keeping the row under a key of its own: a membership by its
identifier, and by its person within its organization. A change to a row invalidates its entry under every one, as the
row stood before the change and after, its key columns read from the row itself.

`GetMembershipOfQuery.cs`:

```csharp
using Bitwarden.Server.Sdk.Data;
using Bitwarden.Server.Sdk.Resources;
using Bitwarden.Server.Sdk.Resources.Caching;
using Bitwarden.Server.Sdk.Resources.Querying;

public interface IGetMembershipOfQuery
{
    Task<ResourceDocument<Membership>?> ReadCurrentAsync(Fields<Membership> fields = default, CancellationToken cancellationToken = default);
}

public sealed class GetMembershipOfQuery(CacheableQueryServices services)
    : CacheableQuery<MembershipEntity, Membership>(services), IGetMembershipOfQuery
{
    protected override void Configure(CacheableQueryBuilder<MembershipEntity, Membership> query) => query
        .KeyedBy(row => row.UserId, "user")                    // a Guid, or a Guid? a row may not have yet
        .WithinOwner()                                          // the person's membership in this organization
        .NotUnique(rows => rows.OrderBy(row => row.Email).ThenBy(row => row.Id))
        .CurrentFrom(Owner.User)                                // GET memberships/current: the person in context
        .Map(MembershipMapping.ToResource);
}
```

`MembershipsEndpoints.cs`:

```csharp
using Bitwarden.Server.Sdk.Authorization;
using Bitwarden.Server.Sdk.Errors;
using Bitwarden.Server.Sdk.Resources;
using Bitwarden.Server.Sdk.Resources.Querying;

public static class MembershipsEndpoints
{
    [StandardReadOne("current")]
    [Authorization(User = ContextRequirement.Required)]
    public static async Task<ResourceDocument<Membership>> GetCurrentMembership(
        IGetMembershipOfQuery query, Fields<Membership> fields = default, CancellationToken cancellationToken = default)
        => await query.ReadCurrentAsync(fields, cancellationToken) ?? throw new NotFoundException();
}
```

- **Keys.** By identifier, `{service}:{type}:{key}`. By another key, `{service}:{type}:by-{name}:{key}`. By a key
  within its owner (`.WithinOwner()`, the owner the row's `[OwnedBy]` column), `{service}:{type}:by-{name}:{owner}:{key}`,
  the owner's placeholder `{organization}`, `{user}` or `{provider}`. A row whose key is null (a membership nobody has
  accepted) has no entry under it.
- **`current`.** `.CurrentFrom(axis)` reads the key from the party the context names, with `ReadCurrentAsync`; a
  handler taking no key is that read. The contract publishes the template with the party's placeholder in place of
  `{key}` (`organization-service:membership:by-user:{organization}:{user}`), so a client fills it from its call. The
  operation must require that party, and the owner for a key within its owner, or the host refuses to start.
- **Keys several rows may hold.** A key is unique in the schema (with its owner, for one within its owner) unless
  declared `.NotUnique(order)`. Such a read takes up to two rows by the order: one is answered and cached; two are
  answered with the first and nothing is cached; none is "nothing recorded", cached where the owner is known (a key
  within its owner, or a key that is the owner). A row arriving under the key, added or moved there, marks every
  version dead for `Caching:Owned:CommitAllowance`, so reads of that key are answered by the service for that long
  after a row arrives. A key two rows hold is never cached.
- **About an owner the request doesn't name.** `ReadOwnedAsync(owner, key)` is the service's own read of a key that is
  its owner, or is within one, about a party its request doesn't name: an organization's request working out a
  member's provider staff from the provider's rows. Narrowed by the owner named, as row-level security narrows a
  request naming it, checked against it when answered from the entry, and filled as any read is, so the cache holds
  what a client composing the same answer reads.
- **Several owners.** A row `[OwnedBy]` two owners (a provider's link to an organization) is cached for one of them,
  named with `.OwnedBy(owner)`: entries are checked against that owner.
- **Other owners.** `.IdentifiesOwner(owner)` declares a row that is its own owner (an organization, read by its
  identifier); a request naming another owner of that kind is refused `422`. `.Unowned()` declares a row that belongs
  to nobody, read by any holder of the scope.

### Version a row whose writes are last-writer-wins

An entry's version is the row's `Version`, which the platform makes a concurrency token, so a second writer holding
the same version fails in the database. For a row whose writes must stay last-writer-wins, name a column of its own
that nothing but the cache moves: `.VersionedBy("CacheVersion")`, an `int` or `long`. The database moves it on as the
change commits, under the row's lock.

### Include rows of another table

A cacheable query that includes rows of another table (`.Including(rows => rows.Include(group => group.Memberships))`)
builds its entry from them too. A change to one of those rows (added, modified or deleted) moves its parent's version
on in the database as the change commits, so the parent's entry is invalidated too. The context's own copy of the
parent, if it tracks one, takes the version the database holds after the change.

Only a navigation a row's foreign key names its parent by is supported: a collection or a reference on the parent, one
level deep, whose foreign key, a property of the included row, points at the parent's cache key. A navigation to a row
the parent refers to, one through a join table, or an include of an include is refused as the host starts.

### Write in bulk

`ExecuteUpdate` and `ExecuteDelete` pass no row through the change tracker, so nothing would see which entries they
make stale. On a table a cache entry is built from (a declared resource's own, or one its read includes) they are
refused with `InvalidOperationException` as Entity Framework compiles them, with a cache or without: load the rows and
save them, in batches where there are many. An owner's erasure (`OwnerErasure.EraseAsync`), which tells the cache
itself, is let through, and every version of each row it deletes is marked dead.

### Choose what happens when the cache can't be told

A change the cache can't be told of before it commits (Redis down or timing out) would leave its entries readable by
other services until they expire: what it took away still granted by every reader of the old copy, what it gave still
refused. So by default the change is refused: the exception thrown as the transaction commits stops the commit,
nothing is changed, and the request that made it is answered `503` (`ServiceUnavailableException`). A save in a
message handler or a job throws the same, and is tried again as the handler's failures are.

`GetLabelQuery.cs`:

```csharp
using Bitwarden.Server.Sdk.Resources.Caching;

public sealed class GetLabelQuery(CacheableQueryServices services) : CacheableQuery<LabelEntity, Label>(services)
{
    protected override void Configure(CacheableQueryBuilder<LabelEntity, Label> query) => query
        .Map(LabelMapping.ToResource)
        .WhenCacheUnreachable(CacheWrites.Proceed);   // committed with a warning, stale until it expires
}
```

- **Declared on what is cached, not judged per change.** `CacheWrites.Refuse`, the default, or `CacheWrites.Proceed`
  for a resource no reader grants or refuses anything by. A change touching anything declared `Refuse` is refused.
- **Every writer, however it saves.** `SaveChangesAsync`, an explicit transaction or an erasure: no writer can forget
  it. A commit refused and tried again on the same transaction is refused again unless the cache can be told then.
- **Said at startup.** The host logs, for each table something cached is built from, which applies. Where
  declarations reading one table disagree, `Refuse` wins.

### Give each service its Redis user

`ServiceCacheAcl.User` states the Redis ACL rule a service connects as: it writes only its own prefix and reads only
the prefixes of the services it calls. The keyless commands key permissions don't confine (`SCAN`, `RANDOMKEY`,
`DBSIZE`, `FUNCTION`) are taken away, so no service lists another's keys. In a provisioning tool, `CacheUsers.cs`:

```csharp
using Bitwarden.Server.Sdk.Resources.Caching;

public static class CacheUsers
{
    // ACL SETUSER's arguments, or a user directive in a Redis ACL file.
    public static string Lending(string password) =>
        ServiceCacheAcl.User("lending-service", password, ["shelf-service"]);

    // During a rotation: both passwords, then the same rule with the next alone.
    public static string Rotating(string current, string next) =>
        ServiceCacheAcl.User("lending-service", [current, next], ["shelf-service"]);
}
```

## Gotchas and constraints

- **Refused at startup** when a cached copy could not be authorized as the service authorizes the read. The handler
  taking a cacheable query may take nothing but the identifier (`id`) or key (by its `.KeyedBy` name; nothing, for a
  read of `current`), the fieldset, a cancellation token and the query, and must return the resource's document. It
  may have no endpoint filter, no `IAuthorizesOperation`, no policy or requirement of its own, no other credential
  (`[AcceptsOrganizationCredential]`, `[AcceptsSendCredential]`), and may not demand a role, a permission or any of
  several scopes, or be anonymous. The service may not be a product service. The row, and any row its read includes,
  may carry no query filter but row-level security; the read may only include unfiltered navigations; the row must
  have a version and one possible owner; a resource read across owners may cache no `[PersonalData]` attribute; and
  the queries of one table read one resource at one version, each under a key of its own.
- **Register by type.** A query registered by a factory rather than by type isn't found, and refuses to read. A query
  with no handler taking it still fills and invalidates, and publishes nothing.
- **Raw SQL isn't checked**, as it can't be read reliably: `ExecuteSql`, `ExecuteSqlRaw` and their like must never
  write to a table a cache entry is built from. Such a write is never invalidated, and readers of the cache are served
  what it replaced until the entry expires.
- **Switch clients off first.** Switching the cache off in a running deployment should be done on the clients first:
  an owner without Redis cannot invalidate what clients might still read.
- **Not a cache a process keeps for itself.** The owner writes through version-checked scripts a general-purpose cache
  library cannot express, and clients read the one shared copy without a per-process layer. For a cache a process
  keeps for itself, use
  [Bitwarden.Server.Sdk.Caching](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Caching/src/PACKAGE.md)'s
  `AddBitwardenCaching`.

## Types

| Type                                        | What it is                                                                     |
| ------------------------------------------- | ------------------------------------------------------------------------------ |
| `CacheableQuery<TEntity, TResource>`        | The base class of a cacheable read; its read methods are the platform's.       |
| `CacheableQueryBuilder<TEntity, TResource>` | Declares the read: map, key, owner, includes, per-caller attributes, lifetime. |
| `CacheableQueryServices`                    | What a query reads with, given per request by `AddOwnedCache`.                 |
| `CacheWrites`                               | What a change does when the cache can't be told of it: `Refuse` or `Proceed`.  |
| `ServiceCacheAcl`                           | The Redis ACL rule each service connects as.                                   |
| `AddOwnedCache<TContext>()`                 | Gives the service its cache, on `IServiceCollection`.                          |
