# Bitwarden.Server.Sdk.Service.Abstractions

**Audience:** engineers writing a package that extends every Bitwarden service that references it.

## About

Lets a package configure every Bitwarden service that references it, so a registration the package needs can't be
forgotten: an outbox needs its dispatcher, jobs need their runner. A package that extends a service references this
package, not the host. Its only dependencies are Microsoft.Extensions.DependencyInjection.Abstractions and
Microsoft.Extensions.Configuration.Abstractions.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Service.Abstractions
```

## Key concepts

- **Extensions are declared, not discovered.** A package implements `IServiceExtension` and declares it with
  `[assembly: ServiceExtension(typeof(...))]`. Nothing is found by scanning types: an extension exists because the
  attribute says so, where a reviewer sees it.
- **The host applies them.** The host in Bitwarden.Server.Sdk.Service reads `[ServiceExtension]` from the service's
  assembly and every assembly it references, transitively, as it builds the service.
- **After the service's own registrations.** An extension sees what the service registered, and one that uses `TryAdd`
  leaves the service's own registration in place.
- **An extension reads its own settings** from the `IConfiguration` it is given; this package reads none.

## Usage

### Configure every service that references your package

In your package, `AuditingExtension.cs`:

```csharp
using Bitwarden.Server.Sdk.Service;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

[assembly: ServiceExtension(typeof(Widgets.Auditing.AuditingExtension))]

namespace Widgets.Auditing;

public sealed class AuditingExtension : IServiceExtension
{
    public void Configure(IServiceCollection services, IConfiguration configuration)
    {
        if (string.Equals(configuration["Auditing:Enabled"], "false", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Tried, so a service that registered its own audit log keeps it.
        services.TryAddSingleton<IAuditLog, AuditLog>();
    }
}

public interface IAuditLog
{
    void Record(string what);
}

public sealed class AuditLog : IAuditLog
{
    public void Record(string what) => Console.WriteLine(what);
}
```

A service that references `Widgets.Auditing` now has an `IAuditLog` without calling anything.

## Gotchas and constraints

- **Some assembly names are never searched.** The host never looks in assemblies whose names start with `System` or
  `Microsoft`, or in `netstandard` and `mscorlib`, so a package with such a name can't declare one.
- **The compiler drops a reference nothing uses,** so a service must use at least one of the package's types, or its
  extension is never found.
- **The order is fixed but not yours.** Extensions run by assembly name and then by type name. Don't depend on another
  extension running first.
- **A public parameterless constructor is required.** The host creates each extension with it. A declared type that
  doesn't implement `IServiceExtension` stops the service from starting.
- **A host that doesn't apply extensions applies nothing:** there, call the package's own registration method, if it
  has one.

## Types

| Type                        | Purpose                                                      |
| --------------------------- | ------------------------------------------------------------ |
| `IServiceExtension`         | A package's contribution to every service that references it |
| `ServiceExtensionAttribute` | Declares an assembly's extension                             |
