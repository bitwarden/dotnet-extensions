# Bitwarden.Server.Sdk.Infrastructure

Declarations of the infrastructure a service needs.

- `InfrastructureServiceCollectionExtensions` - `AddInfrastructureRequirement(kind, properties)` for declaring requirements
- `InfrastructureOptions` - Options holding the declared requirements as `JsonObject`s

## Design

- A requirement is a `JsonObject` with a `kind` property. There is no separate name or identity:
  the whole object is the identity, and `InfrastructureOptions.Add` skips a requirement when
  `JsonNode.DeepEquals` finds an equal one. Requirements are never merged, so composite identities
  such as a topic and subscription need no encoding.
- `Add` copies the properties it is given, so callers cannot change a requirement after declaring
  it and the same declaration can be collected more than once.
- Each `AddInfrastructureRequirement` call is a single `Configure<InfrastructureOptions>`
  registration. Nothing resolves `InfrastructureOptions` in a running service, so the configure
  delegates never run and the requirements are never created.
- `InfrastructureOptions` is public so libraries can use the options pattern directly, for example
  `Configure<TDep>` to decide at collection time whether a requirement exists.
- No well-known kinds are defined yet. Typed helpers that build the `JsonObject` for a kind can be
  added once kinds have stabilized, without changing the model.
