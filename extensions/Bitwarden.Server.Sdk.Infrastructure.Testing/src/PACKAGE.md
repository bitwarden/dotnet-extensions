# Bitwarden.Server.Sdk.Infrastructure.Testing

## About

Runs a service's startup and returns the infrastructure it declares through
`Bitwarden.Server.Sdk.Infrastructure`.

## Usage

```csharp
var requirements = InfrastructureCollector.CollectInfrastructure<Program>();
```

Collect once per hosting mode to get a separate list for cloud and for self-hosted:

```csharp
[Theory]
[InlineData(false)]
[InlineData(true)]
public void Infrastructure(bool selfHosted)
{
    var requirements = InfrastructureCollector.CollectInfrastructure<Program>(o => o.SelfHosted = selfHosted);

    // Assert on or snapshot the requirements.
}
```

### Options

- `SelfHosted` - Sets `globalSettings:selfHosted`. Defaults to `false` (cloud).
- `Environment` - The hosting environment. Defaults to `Production` so the result matches what ships.
- `Configuration` - Additional configuration values for the service.

All of these are applied as host configuration, so they are visible to code that reads
configuration while `Program` builds the host.

### How the service is run

The service is started through `WebApplicationFactory`, with its hosted services and startup
options validation removed. It does not connect to any infrastructure and does not need valid
configuration for it. Requirements declared from inside a hosted service are therefore not
collected.

Each requirement is a `JsonObject`, which makes the result easy to snapshot:

```json
{ "kind": "messaging-subscription", "topic": "orders", "subscription": "billing" }
```
