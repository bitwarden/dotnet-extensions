# Restricted dependencies

Freezes a type's usage against a committed baseline, so it can only shrink.

The package is three projects:

- **`src/`** — this project. The packable library: the baseline document format (`BudgetModel`, `BudgetEntry`) and the shrink-only comparison (`BudgetRatchet`) a consuming repository's own tooling uses.
- **`analyzers/Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers`** — the analyzer and the generator that emits the attributes.
- **`contracts/Bitwarden.Server.Sdk.RestrictedDependencies.Contracts`** — the netstandard2.0 types both of the above need: the analyzer-config keys, the diagnostic ids, the observation property names, and the document format itself. It cannot live in `src`, because `src` references the analyzer *as an analyzer* (`ReferenceOutputAssembly="false"`), so nothing in `src` is visible to it. Its DLL is therefore packed twice — into `lib/netstandard2.0` for the consuming tool and into `analyzers/dotnet/cs` for the analyzer beside it (see the `None` items in `Bitwarden.Server.Sdk.RestrictedDependencies.csproj`).

See [PACKAGE.md](./PACKAGE.md) for how to turn it on, and [docs/diagnostics.md](https://github.com/bitwarden/dotnet-extensions/blob/main/docs/diagnostics.md) for the diagnostics.
