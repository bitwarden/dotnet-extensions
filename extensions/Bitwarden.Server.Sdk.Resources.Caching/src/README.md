# Bitwarden.Server.Sdk.Resources.Caching

**Audience:** engineers changing this package.

The owning side of a domain service's cache in Redis: filling entries on read, invalidating them before a change
commits, and publishing what clients may read. See [PACKAGE.md](PACKAGE.md) for how to use it.

## What belongs here

What the owner of a cache does: declare, fill, invalidate and publish its entries, and refuse at startup what a client
of the cache could not repeat. The key layout, hash fields and settings shared with clients belong in
Bitwarden.Server.Sdk.Resources.Caching.Abstractions, and reading another service's entries belongs in
Bitwarden.Server.Sdk.Client. Nothing here may let a cached copy answer a read the service itself would refuse.

## How it works

| File                                                        | What it does                                                                                                   |
| ----------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| `CacheableQuery.cs`                                         | The read path: own entry, else the row through the context, then the fill and the per-caller attributes.       |
| `CacheableQueryBuilder.cs`, `CacheableResource.cs`          | The declaration, and the resource it settles to: keys, owner, version column, includes, lifetime.              |
| `CacheableQueries.cs`                                       | Finds the queries among the registrations by type, and checks the queries of one table agree.                  |
| `CacheContract.cs`, `CacheContractCheck.cs`                 | Settles what each operation taking a query demands, and refuses at startup what a client could not repeat.     |
| `CacheContractTransformer.cs`                               | Writes `x-bitwarden-cache` into the OpenAPI operation.                                                         |
| `CacheInvalidationInterceptor.cs`, `CacheChanges.cs`        | Records, on each save, the entries the change makes stale and the versions it makes dead.                      |
| `PendingInvalidations.cs`, `CacheTransactionInterceptor.cs` | Holds what a context's saves recorded, and writes the tombstones in `TransactionCommitting`.                   |
| `CacheBulkWriteGuard.cs`, `CacheErasureObserver.cs`         | Refuses `ExecuteUpdate` and `ExecuteDelete` on a table an entry is built from; invalidates an owner's erasure. |
| `IncludedRows.cs`, `VersionBump.cs`                         | Reads a query's includes, and moves a parent's version when an included row changes.                           |
| `RedisOwnedCacheStore.cs`                                   | The version-checked Redis scripts, and the eviction policy check.                                              |
| `OwnedCacheTimes.cs`, `OwnedCacheStartup.cs`                | The fill limit and commit allowance, and the startup checks of them and of query filters.                      |
| `ServiceCacheAcl.cs`, `ServiceCacheUserCheck.cs`            | The Redis ACL rule per service, and the startup check that a connection names a user of its own.               |
| `CacheCascadeCheck.cs`                                      | Finds the database cascades a cached read's tables depend on, and refuses or warns about undeclared ones.      |

- **Invalidation before commit.** Every save outside a transaction is made to open one, even for a single statement,
  so every change commits through `TransactionCommitting`, after its statements have run. A change that rolls back
  drops what it recorded; one whose commit fails after the tombstones were written leaves them, which only turns reads
  into misses until they expire. An erasure deletes in bulk, so its rows are invalidated as the erasure's transaction
  commits (`IErasureObserver`), every version of each dead, as a bulk delete checks no version.
- **The dead version.** A tombstone marks dead a modified or deleted row's version before the change, and "nothing
  recorded" for a unique key a row arrives at or moves to. A fill of the dead version, or anything older, is refused;
  the version the change commits is accepted. The interceptor moves the version of a modified row the code did not
  move, and the version is the row's concurrency token (`VersionConcurrency`), so two changes can't both mark one
  version dead and commit different successors. A version that is no concurrency token (`.VersionedBy(...)`) is moved
  by the database instead, `SET version = version + 1` under the row's lock as the change commits, and the version it
  moved from is the dead one. A change to an included row moves its parent's version the same way, so two changes to
  its rows at once each move it once.
- **The fill script.** A fill of exactly what the entry already holds, at the same version, writes nothing, and a
  different entry at the same version is left too; one in another format is replaced.
- **Tombstone lifetimes.** Every tombstone lives as long as the longest-lived entry, which `OwnedCacheStartup` checks
  outlives the fill limit plus the commit allowance. An arrival's tombstone (a row arriving under a key several rows
  may hold) names no version, so it lives only the commit allowance: it must refuse every fill read before the arrival
  committed, and those arrive within the allowance. A tombstone already there keeps the rest of its own lifetime.
- **Eviction.** An evicted tombstone lets an old row back in, so `RedisOwnedCacheStore` reads `INFO memory` once per
  process before it fills, as every service's user may run `INFO` where `CONFIG GET` is refused.
- **The ACL rule has one home.** `ServiceCacheAcl`'s constants (`Commands`, `Channels`, `OwnKeys`, `ReadKeys`) are the
  rule; a tool writing an ACL file without .NET reads them from the source. Redis refuses a write to a key the user may
  only read, and a script runs under the caller's key permissions, so the owner's scripts cannot reach another prefix.
  The only channel granted is the one StackExchange.Redis watches for a primary changing: there is no backplane.
- **Database cascades.** Invalidation reads the change tracker (`CacheInvalidationInterceptor.Record`), which never
  sees a row the database deletes or changes through `ON DELETE CASCADE` or `SET NULL`. `CacheCascadeCheck` reads every
  such foreign key from the model, on the read's own table and those it includes, at startup, with a cache or without,
  and matches each against the read's declarations (`CascadeCoverage`). `ParentInvalidated` is checked against the
  model: the key must be the include's own. `DependentsLoaded` can't be, so it carries a reason. Undeclared keys refuse
  the host in `Development` and `Testing`, and are logged once elsewhere, so a deployment whose model gains a key
  isn't stopped by it.

## Known limitations

- **What the model doesn't say.** The cascade check reads the Entity Framework model. A foreign key or trigger the
  database has and the model doesn't, or one whose delete behaviour differs from the model's, isn't seen.
- **What the cascade check doesn't read.** It reads the foreign keys of the read's own entity type and of the types its
  includes reach, with those they inherit: not keys declared only on a derived type, nor an owned type's own keys. A key
  on a shadow property, or on a shared-type entity, can't be named by `CascadeCovered`, so it can only be reported.

## Testing

Tests are in `tests/Bitwarden.Server.Sdk.Resources.Caching.Tests` and need Docker, for Redis. A test class is named for
the source file it tests; `OwnedCacheTests`, `MultiKeyTests` and `RedisAclTests` run the whole cache against a host
and a Redis, rather than one type.
`tests/Bitwarden.Server.Sdk.Resources.Caching.Generated.Tests` compiles the cacheable read tests again with the request
delegate generator, as a service compiles its handlers; every test must pass both ways. Run them from the repository
root:

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Resources.Caching/tests/Bitwarden.Server.Sdk.Resources.Caching.Tests
dotnet test --project extensions/Bitwarden.Server.Sdk.Resources.Caching/tests/Bitwarden.Server.Sdk.Resources.Caching.Generated.Tests
```

A change must keep:

- **Never older than what committed.** No fill may write a version a tombstone marks dead, or overwrite a newer one,
  and none is made where Redis may evict a tombstone, or after the fill limit.
- **Invalidation before the commit.** Every change to what the cache holds commits in a transaction whose
  `TransactionCommitting` tombstones it first, and a change the cache can't be told of, declared `Refuse`, never
  commits. The dead version is one the database vouches for: a concurrency token, or a version the database moves.
- **No write the cache can't see.** A write in bulk to a table an entry is built from is refused unless it is an owner's
  erasure, and a change to an included row moves its parent's version.
- **No cascade undeclared.** A foreign key that lets the database delete or change a row an entry is built from is
  declared on the read, and `ParentInvalidated` is accepted only where it is true.
- **Startup refusals.** A cached copy that could not be authorized as the service authorizes the read stops the service.
- **The key and field formats.** They are shared with clients deployed apart from the owner. A change to what an entry
  holds or how changes `OwnedCacheEntry.CurrentFormat`, so clients that don't know it miss rather than misread.
