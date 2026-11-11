# Bitwarden.Server.Sdk.Service.Abstractions

**Audience:** engineers changing this package.

The contract between a package that configures every service referencing it and the host that applies it. See
[PACKAGE.md](PACKAGE.md) for how to use it.

## What belongs here

Only what a package that extends a service and the host that applies the extension both need. A package declaring an
extension references this package, so it must never have to reference the host: no dependencies beyond the two
abstractions `Configure` takes.

## How it works

The package has no logic. Finding and applying extensions belongs to the host, in
`Bitwarden.Server.Sdk.Service/src/ServiceExtensions.cs`, called from `Service.Build`, and is tested there.

Extensions are declared, not discovered. A change must keep it so: an extension exists because a package author wrote
the attribute.

`ServiceExtensionAttribute` carries `[DynamicallyAccessedMembers(PublicParameterlessConstructor)]` on the type it takes,
so the host's `Activator.CreateInstance` survives trimming.

## Testing

The tests are in `tests/Bitwarden.Server.Sdk.Service.Abstractions.Tests`. `IServiceExtension` has no logic and no
test class.

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Service.Abstractions/tests/Bitwarden.Server.Sdk.Service.Abstractions.Tests
```
