# Bitwarden.Server.Sdk.Infrastructure

## About

Lets libraries and services declare the infrastructure a service needs to run, such as databases,
distributed caches, message topics and subscriptions, configuration, or certificates. The
declarations can then be collected from a service's startup to produce a complete list of what the
service needs, without changing how the service behaves at runtime.

## Usage

A requirement is a JSON object with a `kind` and any properties that describe it. Declare one while
registering services:

```csharp
builder.Services.AddInfrastructureRequirement("database", new JsonObject { ["name"] = "vault" });
builder.Services.AddInfrastructureRequirement("messaging-subscription", new JsonObject
{
    ["topic"] = "orders",
    ["subscription"] = "billing",
});
builder.Services.AddInfrastructureRequirement("distributed-cache");
```

These are collected as:

```json
{ "kind": "database", "name": "vault" }
{ "kind": "messaging-subscription", "topic": "orders", "subscription": "billing" }
{ "kind": "distributed-cache" }
```

### Duplicates

The whole object is a requirement's identity. Declaring a deeply equal requirement more than once,
for example from both a library and the service, results in a single requirement. Property order
does not matter, but array order does. A requirement that differs in any property, including an
extra one, is kept as a separate requirement, so every declaration of the same requirement should
describe it the same way.

### Requirements that depend on other services

Requirements are collected in `InfrastructureOptions`. When a requirement, or whether it exists at
all, depends on other services, configure the options directly with the options pattern. This works
from libraries that only see an `IServiceCollection`:

```csharp
services.AddOptions<InfrastructureOptions>()
    .Configure<IBitwardenEnvironment>((infrastructure, environment) =>
    {
        if (!environment.SelfHosted)
        {
            infrastructure.Add("distributed-cache");
        }
    });
```

`IBitwardenEnvironment.SelfHosted` comes from `SelfHostDetails`, so this relies on the service
configuring `SelfHostDetails` from `globalSettings:selfHosted` as described in the
`Bitwarden.Server.Sdk` documentation.

### Writing declarations

- Declarations are only evaluated when infrastructure is being collected, after the service
  provider is built. Hosted services are not started and startup validation does not run, but
  resolving options from a `Configure<TDep>` callback still runs their validation.
- Never record secrets such as connection strings or keys, since collected requirements are often
  snapshotted. Record the configuration key name instead.
- Declare what the service needs for the way it is currently configured. Requirements that differ
  between cloud and self-hosted are captured by collecting once per hosting mode, not by tagging
  them, so the collected list depends on the configuration the collector runs with.
- `kind` is reserved and cannot be used as a property name.

### Collecting infrastructure

Use `Bitwarden.Server.Sdk.Infrastructure.Testing` to run a service's startup and collect the
requirements it declares.
