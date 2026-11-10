# Bitwarden.Server.Sdk.Service.Abstractions

## About

How a package configures every Bitwarden service that references it, so a behavior the package needs can't be
forgotten: `IServiceExtension`, declared with `[assembly: ServiceExtension(typeof(...))]`, and applied by a service
host as it builds each service.

A package that extends a service references this, not the host. Its only dependencies are
Microsoft.Extensions.DependencyInjection.Abstractions and Microsoft.Extensions.Configuration.Abstractions.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Service.Abstractions
```

## Configuration

The package reads no settings. An extension reads its own from the `IConfiguration` it is given.

## Getting started

Declare the extension on the assembly, and register what every service needs:

```csharp
using Bitwarden.Server.Sdk.Service;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

[assembly: ServiceExtension(typeof(Widgets.Metering.MeteringExtension))]

namespace Widgets.Metering;

public sealed class MeteringExtension : IServiceExtension
{
    // Tried, so a service that registered its own meter keeps it.
    public void Configure(IServiceCollection services, IConfiguration configuration) =>
        services.TryAddSingleton<IWidgetMeter, WidgetMeter>();
}

public interface IWidgetMeter
{
    void Count(string what);
}

public sealed class WidgetMeter : IWidgetMeter
{
    public void Count(string what)
    {
    }
}
```

See the
[README](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service.Abstractions/src/README.md)
for how a host applies extensions.

## Documentation

- A service host reads `[ServiceExtension]` from the service's assembly and the assemblies it references.
- It applies them after the service's own registrations, so an extension sees them, and one that uses `TryAdd` keeps
  them.
- An extension needs a public parameterless constructor.

| Type                        | Purpose                                                      |
| --------------------------- | ------------------------------------------------------------ |
| `IServiceExtension`         | A package's contribution to every service that references it |
| `ServiceExtensionAttribute` | Declares an assembly's extension                             |
