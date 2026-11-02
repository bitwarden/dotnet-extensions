# Bitwarden.Server.Sdk.Infrastructure

## About

Lets libraries and services declare the infrastructure a service needs to run, such as databases,
distributed caches, message topics and subscriptions, configuration, or certificates. The
declarations can then be collected from a service's startup to produce a complete list of what the
service needs, without changing how the service behaves at runtime.

## Usage

A requirement has a kind and a JSON object of properties that describe it. Declare one while
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

These are collected grouped by kind, with kinds in alphabetical order:

```json
{
  "database": [ { "name": "vault" } ],
  "distributed-cache": [ {} ],
  "messaging-subscription": [ { "topic": "orders", "subscription": "billing" } ]
}
```

### Duplicates

Within a kind, the properties are a requirement's identity. Declaring the same kind with deeply
equal properties more than once, for example from both a library and the service, results in a
single requirement. Property order does not matter, but array order does. A requirement that differs
in any property, including an extra one, is kept as a separate requirement, so every declaration of
the same requirement should describe it the same way.

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

`IBitwardenEnvironment.SelfHosted` comes from `SelfHostDetails`, which the service configures from
`globalSettings:selfHosted` as described in the `Bitwarden.Server.Sdk` documentation. When collecting,
`Bitwarden.Server.Sdk.Infrastructure.Testing` sets `SelfHostDetails` directly for each hosting mode.
Prefer this over reading `globalSettings:selfHosted` from configuration while registering services.

### Writing declarations

- Declarations are only evaluated when infrastructure is being collected, after the service
  provider is built. What else runs during collection depends on the collector:
  `Bitwarden.Server.Sdk.Infrastructure.Testing` runs the service's full startup, as described in its
  "How the service is run" section. Resolving options from a `Configure<TDep>` callback runs their
  validation.
- Never record secrets such as connection strings or keys, since collected requirements are often
  snapshotted. Record the configuration key name instead.
- Declare what the service needs for the way it is currently configured. Requirements that differ
  between cloud and self-hosted are captured by collecting once per hosting mode, not by tagging
  them, so the collected list depends on the configuration the collector runs with.

### Collecting infrastructure

Use `Bitwarden.Server.Sdk.Infrastructure.Testing` to run a service's startup and collect the
requirements it declares.
