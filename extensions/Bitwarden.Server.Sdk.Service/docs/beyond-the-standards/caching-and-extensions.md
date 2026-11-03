# Caching and extensions

Two small pieces of the platform: a short in-process memo for another service's answer, and the way
a package extends every service that references it.

## What the package does

- **`GetOrResolveOnceAsync`** (`Bitwarden.Server.Sdk.Service.Caching`), an extension on
  `IMemoryCache`, keeps one answer per key for a lifetime you choose. Concurrent requests that miss
  together share one resolution, unlike `IMemoryCache.GetOrCreateAsync`, which runs it once per
  caller.
- **Failures are not kept.** A failed or cancelled resolution is replaced on the next call, and no
  caller's cancellation token reaches the shared resolution, so one caller giving up fails nobody
  else: each caller's token only stops that caller waiting.
- **`IServiceExtension`** (Bitwarden.Server.Sdk.Common) is a package's contribution to every service
  that references it, declared with `[assembly: ServiceExtension(typeof(...))]`.
- **Extensions run after `ConfigureServices`,** so an extension can see what the service registered,
  in a fixed order: by assembly name, then by type name. A declared type that doesn't implement
  `IServiceExtension` stops the service at startup.
- **Only referenced assemblies are read.** The package walks the service's assembly and what it
  references, transitively, skipping `System*`, `Microsoft*`, `netstandard` and `mscorlib`.

## Example

### Cache another service's answer briefly

```csharp
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Caching;
using Microsoft.Extensions.Caching.Memory;

internal sealed class WidgetService(string[] args) : Service("widget-service", "Widget Service", args)
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<IWidgetPolicies, WidgetPoliciesClient>();
        services.AddSingleton<WidgetGate>();
    }
}

internal sealed class WidgetGate(IMemoryCache cache, IWidgetPolicies policies)
{
    public Task<bool> IsEnabledAsync(Guid organizationId, CancellationToken cancellationToken) =>
        // Concurrent requests that miss together share one call to the policy service, and the
        // answer is kept for 30 seconds. A failed or cancelled call is not kept.
        cache.GetOrResolveOnceAsync(
            ("widget-enabled", organizationId),
            TimeSpan.FromSeconds(30),
            () => policies.IsWidgetEnabledAsync(organizationId),
            cancellationToken);
}
```

Key the entry by everything the answer depends on (here the organization), so one caller's answer is
never served to another.

### Extend every service from a package

```csharp
using Bitwarden.Server.Sdk.Common;
using Microsoft.Extensions.DependencyInjection.Extensions;

[assembly: ServiceExtension(typeof(WidgetAuditExtension))]

// A package's contribution to every service that references it.
public sealed class WidgetAuditExtension : IServiceExtension
{
    public void Configure(IServiceCollection services, IConfiguration configuration)
    {
        // TryAdd, so a service that registered its own sink first keeps it.
        services.TryAddSingleton<IWidgetAuditSink, LoggingWidgetAuditSink>();
    }
}
```

The type needs a public parameterless constructor. The declaration is on the assembly rather than
discovered by scanning, so an extension is something a package author states on purpose, where a
reviewer sees it.

### Other extension points

A package that adds to the standard endpoints has three more seams, in
`Bitwarden.Server.Sdk.Service.Endpoints` and `StandardResponses`:

- **`IStandardEndpointFilterProvider`** adds an endpoint filter to every standard endpoint it
  applies to, between the platform's checks and the handler. It is asked once per endpoint as it is
  built, and can report a problem that stops the service starting.
- **`IStandardEndpointCheck`** is a rule checked over every endpoint once all are built, reported
  with the startup check's other problems.
- **`ServedByPlatform<T>()`** is the body of a `[StandardReadOne]` handler whose read a registered
  filter answers. The handler still declares the route, operation and documentation; its body throws
  if nothing answered.

## Configuration

Neither has settings of its own. An extension reads whatever configuration it needs from the
`IConfiguration` it is given.

## What you decide

- **What to cache here.** Use `GetOrResolveOnceAsync` only for answers from other services. An
  answer read from the request's own database belongs to that request's scope.
- **Anything bigger.** It is a short in-process memo of one answer per key. For a cache shared
  between instances, or one that must be invalidated, use Bitwarden.Server.Sdk.Caching.
- **Register `IMemoryCache`.** The package doesn't register one for you; call
  `services.AddMemoryCache()`.
- **Keep the reference real.** An extension is found only when the service's compiled assembly
  references the package's assembly. The C# compiler drops a reference nothing uses, so a package
  whose types the service never touches is never read.
- **Let the service win.** Register with `TryAdd...` in an extension, so a service that registered
  its own implementation first keeps it.
- **Zero knowledge.** Never cache Vault Data in plaintext. A service holds it only as Protected
  Data, and nothing the platform caches inspects it.

## See also

- [Defining a service](defining-a-service.md)
- [Outbound HTTP](outbound-http.md)
- [API standards](https://contributing.bitwarden.com/architecture/server/apis)
