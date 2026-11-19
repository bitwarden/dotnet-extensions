# Bitwarden.Server.Sdk.Infrastructure.Testing

## About

Runs a service's startup and returns the infrastructure it declares through
`Bitwarden.Server.Sdk.Infrastructure`.

## Usage

```csharp
var manifest = InfrastructureCollector.CollectInfrastructure<Program>();
```

Collect once per hosting mode and check each against a committed snapshot:

```csharp
[Theory]
[InlineData(false, "infrastructure.cloud.json")]
[InlineData(true, "infrastructure.self-hosted.json")]
public void Infrastructure(bool selfHosted, string snapshot)
{
    var manifest = InfrastructureCollector.CollectInfrastructure<Program>(o => o.SelfHosted = selfHosted);

    manifest.VerifySnapshot(snapshot);
}
```

### Snapshots

`VerifySnapshot(path)` compares the manifest with a snapshot file. A relative path is resolved
against the directory of the test's source file, so snapshots sit next to the test that checks them.

- If the snapshot matches, the test passes.
- If it is missing or out of date, the current manifest is written to it and
  `InfrastructureSnapshotMismatchException` is thrown. Review the change and commit the file; the
  next run passes.

Committing the snapshots makes every change to a service's infrastructure show up as a diff in the
pull request that caused it, and the committed files are always the service's current bill of
materials. Snapshots are written with `\n` line endings, and line endings in an existing snapshot
are ignored, so they match on every platform.

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
