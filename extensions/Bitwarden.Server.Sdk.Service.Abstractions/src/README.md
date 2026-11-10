# Bitwarden.Server.Sdk.Service.Abstractions

Let a package configure every Bitwarden service that references it, so a behavior it needs can't be forgotten.

Some packages need something registered in every service that uses them: an outbox needs its dispatcher, jobs need
their runner. Rather than ask each service to remember a call, the package declares an extension, and the service host
applies it.

- **An extension.** `IServiceExtension.Configure(services, configuration)` adds the package's registrations.
- **Declared, not discovered.** `[assembly: ServiceExtension(typeof(...))]` states the extension where a reviewer sees
  it. Nothing is found by scanning types.

## What belongs here

Only what a package that extends a service and the host that applies the extension both need. A package declaring an
extension references this package, not the host. Its only dependencies are
Microsoft.Extensions.DependencyInjection.Abstractions and Microsoft.Extensions.Configuration.Abstractions, the two
types `Configure` takes.

## Getting started

In the package that extends every service:

```bash
dotnet new classlib -n Widgets.Auditing
cd Widgets.Auditing
dotnet add package Bitwarden.Server.Sdk.Service.Abstractions --prerelease
```

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

A service that references `Widgets.Auditing`, on a host that applies extensions, now has an `IAuditLog` without calling
anything.

## How a host applies extensions

This package only declares extensions. A service host that applies them, as it builds a service:

- reads `[ServiceExtension]` from the service's assembly and the assemblies it references;
- applies them after the service's own registrations, so an extension sees what the service registered, and one that
  uses `TryAdd` leaves the service's own registration in place;
- creates each extension with its public parameterless constructor.

So write an extension to run after the service's registrations, in any order relative to other extensions, and give it
a public parameterless constructor. A host that doesn't apply extensions applies nothing: call the package's own
registration method instead, if it has one.

## Configuration

The package reads no settings. An extension reads its own from the `IConfiguration` it is given.

## Requirements

- .NET 10.
- Microsoft.Extensions.DependencyInjection.Abstractions and Microsoft.Extensions.Configuration.Abstractions.

## Contributing

The tests are in [`tests/Bitwarden.Server.Sdk.Service.Abstractions.Tests`](../tests/Bitwarden.Server.Sdk.Service.Abstractions.Tests).
`IServiceExtension` has no logic and no test class; how extensions are found and applied is the host's, and tested
with it. Run them from the repository root:

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Service.Abstractions/tests/Bitwarden.Server.Sdk.Service.Abstractions.Tests
```

A change must keep:

- **Declaration over discovery.** An extension exists because a package author wrote the attribute.
- **Few dependencies.** A package that extends a service must not have to reference the host.
