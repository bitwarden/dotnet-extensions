# Bitwarden.Server.Sdk.Infrastructure.Testing

## About

Runs a service's startup and returns the infrastructure it declares through
`Bitwarden.Server.Sdk.Infrastructure`.

## Usage

```csharp
var manifest = InfrastructureCollector.CollectInfrastructure<Program>();
```

Collect once per hosting mode to get a separate list for cloud and for self-hosted:

```csharp
[Theory]
[InlineData(false)]
[InlineData(true)]
public void Infrastructure(bool selfHosted)
{
    var manifest = InfrastructureCollector.CollectInfrastructure<Program>(o => o.SelfHosted = selfHosted);

    // Assert on manifest.Requirements, or snapshot manifest.ToJsonString().
}
```

### Options

- `SelfHosted` - Whether `IBitwardenEnvironment` reports the service as self-hosted. Defaults to
  `false` (cloud).
- `SelfHostFlavor` - The flavor `IBitwardenEnvironment` reports, for example `lite`. Requires
  `SelfHosted`; when unset the flavor is `unknown`.
- `Environment` - The hosting environment. Defaults to `Production` so the result matches what ships.
- `Configuration` - Additional configuration values for the service.

`SelfHosted` and `SelfHostFlavor` are applied by post-configuring `SelfHostDetails`, which overrides
however the service sets it up. `IBitwardenEnvironment` is registered if the service has not done so.
Declare requirements that depend on the hosting mode by configuring `InfrastructureOptions` with
`IBitwardenEnvironment`. Code that reads `globalSettings:selfHosted` from configuration while
registering services does not see the hosting mode.

`Environment` and `Configuration` are applied as host configuration, so they are visible while
`Program` builds the host.

To get one list per self-host flavor, collect once per flavor:

```csharp
var lite = InfrastructureCollector.CollectInfrastructure<Program>(o =>
{
    o.SelfHosted = true;
    o.SelfHostFlavor = "lite";
});
```

### How the service is run

The service is started through `WebApplicationFactory`. No requests are served and no ports are
opened, but all of `Program` runs, including:

- hosted services and startup options validation (`ValidateOnStart`)
- code after `builder.Build()`, such as migrations, seeding or cache warm-up
- building the request pipeline, which runs middleware constructors, `app.Use` factories and startup
  filters

The service therefore needs whatever configuration that startup requires, provided through
`Configuration`, and anything it connects to at startup must be reachable or tolerate being
unavailable. Declare requirements while registering services, not from code that runs at startup.

Only web services are supported. Services built with `Host.CreateApplicationBuilder` and no web host
cannot be collected.

Because the hosting mode is not set in configuration, startup behaviour that depends on
`globalSettings:selfHosted`, such as `UseBitwardenSdk` loading `appsettings.SelfHosted.json`, runs
as cloud. Set those values through `Configuration` if a declaration depends on them.

### The manifest

`CollectInfrastructure` returns an `InfrastructureManifest` holding the requirements and the hosting
mode they were collected for. `ToJsonString()` writes it with a fixed property order, ready to save
or snapshot:

```json
{
  "schemaVersion": 1,
  "selfHosted": true,
  "selfHostFlavor": "lite",
  "environment": "Production",
  "requirements": {
    "database": [ { "name": "vault" } ],
    "messaging-subscription": [ { "topic": "orders", "subscription": "billing" } ]
  }
}
```

Requirements are grouped by kind, with kinds in alphabetical order and each kind's requirements in
the order they were declared, so adding a requirement only changes its own kind's section.
`schemaVersion` changes when the shape of the manifest or its requirements changes in a way readers
need to know about. `Configuration` values are never written, since they may hold secrets.
