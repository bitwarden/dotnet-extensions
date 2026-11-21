# Bitwarden.Server.Sdk.Resources.Caching.Abstractions

**Audience:** engineers writing a Bitwarden service that fills a shared cache of its resources, or a client that reads
one.

## About

The keys, hash fields and settings that let a service's callers read some of its resources straight from a shared
Redis instead of calling it, agreed by the owner and its callers without one referencing the other. Nothing here
connects to Redis. It depends only on Microsoft.Extensions.Configuration.Abstractions and
Bitwarden.Server.Sdk.CryptoPrimitives, for `CacheSecretSetting`.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Resources.Caching.Abstractions
```

## Configuration

| Setting                                            | Read by                     | Default | What it does                                                                                                                     |
| -------------------------------------------------- | --------------------------- | ------- | -------------------------------------------------------------------------------------------------------------------------------- |
| `Caching:Owned:Redis:Configuration`                | A service host and a client | None    | The service cache's Redis connection. Unset, there is no service cache: a host fills nothing and a client always asks the owner. |
| `Caching:Owned:Resources:{service}:{type}:Enabled` | A service host and a client | `true`  | `false` tells the owner's host not to fill that resource, and its clients not to read it, so they ask the owner.                 |

The keys are public constants: `OwnedCacheEntry.ConnectionKey` and `OwnedCacheEntry.ResourcesSection`.

## Key concepts

- **The owner fills, its callers read.** The service that owns the data fills entries under its own prefix,
  `OwnedCacheEntry.Prefix(service)`, and a host writes only there. `OwnedCacheEntry` holds the key layout, the hash fields
  and the settings.
- **Each resource is one Redis hash:**

| Field  | Holds                                                                                                       |
| ------ | ----------------------------------------------------------------------------------------------------------- |
| `v`    | The row's version when it was read, or when it changed                                                      |
| `o`    | The row's owner, as `{"organization":"..."}`; absent for a row nobody owns                                  |
| `d`    | The resource as the service serves it, less what it works out per caller; a cached read-many's, their array |
| `f`    | The format a resource's entry is written in, `OwnedCacheEntry.CurrentFormat`                                |
| `none` | Nothing is recorded under the key                                                                           |
| `gone` | A tombstone: the row changed or was deleted at version `v`                                                  |

## Usage

### Key a cached resource by its identifier

`WidgetCacheKeys.cs`:

```csharp
using Bitwarden.Server.Sdk.Resources.Caching;

public static class WidgetCacheKeys
{
    public static string For(Guid widget) =>
        OwnedCacheEntry.Key(OwnedCacheEntry.Template("widget-service", "widget"), widget);
}
```

`WidgetCacheKeys.For(id)` is `widget-service:widget:{id}`. A service or resource type may not contain `:`, `{` or `}`, and the
resource types `answer`, `index`, `gen` and `idempotency` are reserved for the service's other keys; `Template` throws
`ArgumentException` for either.

### Key a cached resource by another key

A resource read by a key other than its identifier gets its own template, so its entries never mix with those by
identifier. A key that names a row only within its owner also names the owner's kind, which the reader fills from the
party its call carries.

`WidgetByMakerCacheKeys.cs`:

```csharp
using Bitwarden.Server.Sdk.Resources.Caching;

public static class WidgetByMakerCacheKeys
{
    private static readonly string Template =
        OwnedCacheEntry.KeyedTemplate("widget-service", "widget", "maker", "organization");

    public static string? For(Guid organization, Guid maker) =>
        OwnedCacheEntry.Key(Template, maker, organization, user: null, provider: null);
}
```

The template is `widget-service:widget:by-maker:{organization}:{key}`. The owner's kind is `organization`, `user` or
`provider`, filled from `{organization}`, `{user}` or `{provider}`. `Key` returns null when the template names a party
that isn't given, or is given as `Guid.Empty`: there is then no entry to read, so ask the owner.

### Key a cached read-many

A cached read-many is one entry holding every resource of the party a call carries, in one order. Its resource is its
own, so the template names only the resource and the party, which a reader fills from its call.

`OwnWidgetCacheKeys.cs`:

```csharp
using Bitwarden.Server.Sdk.Resources.Caching;

public static class OwnWidgetCacheKeys
{
    private static readonly string Template = OwnedCacheEntry.ReadManyTemplate("widget-service", "own-widget", "user");

    public static string? For(Guid user) => OwnedCacheEntry.Key(Template, null, null, user, null);
}
```

The template is `widget-service:own-widget:read-many:{user}`, and the entry's `d` is the array of resources in the
read's order.

### Read a secret that derives keys

A secret that derives keys with `CacheKey` from Bitwarden.Server.Sdk.CryptoPrimitives is read with
`CacheSecretSetting`, under a setting the caller names.

`WidgetSecret.cs`:

```csharp
using Bitwarden.Server.Sdk.CryptoPrimitives;
using Bitwarden.Server.Sdk.Resources.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

public static class WidgetSecret
{
    public static CacheSecret Read(IConfiguration configuration, IHostEnvironment environment) =>
        CacheSecretSetting.Read(configuration, "Widgets:Secret", environment.EnvironmentName);
}
```

The setting holds base64 of 32 bytes. In `Development` an unset secret is `CacheSecret.Development`. Elsewhere `Read`
throws `InvalidOperationException` when it is unset or is the development secret, which is public. Text that is not
base64 of 32 bytes throws everywhere. Each message names the setting, never its value.

## Gotchas and constraints

- **Check the owner beside the data.** A reader checks `o` itself and never trusts the payload for whose row it is.
- **A tombstone is a miss.** A host also refuses a fill of any older version under it, so a slow read can't put back a
  value a later change replaced.
- **What an entry holds is the owner's choice.** This package names the fields, not what goes in `d`. An owner fills an
  entry only with attributes every reader of the cache may see: it is Data at Rest in Redis, readable by anyone with
  read access to the owner's prefix.
- **One secret per purpose.** Give each use of `CacheSecretSetting` its own setting, so a leaked secret exposes only the
  keys of that purpose.

## Types

| Type                 | Purpose                                                                                                                |
| -------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| `OwnedCacheEntry`    | How a service-owned cache entry is stored in Redis, and the settings it uses                                           |
| `CacheSecretSetting` | Reads any secret that derives keys with `CacheKey`: required outside `Development`, never the development secret there |
