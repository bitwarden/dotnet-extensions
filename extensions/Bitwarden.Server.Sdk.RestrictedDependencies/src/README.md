# Restricted dependencies

Freezes a type's usage against a committed baseline, so it can only shrink.

The package is two projects:

- **`src/`** — this project. The library: the baseline document format (`BudgetModel`, `BudgetEntry`) and the shrink-only comparison (`BudgetRatchet`) a consuming repository's own tooling uses.
- **`contracts/Bitwarden.Server.Sdk.RestrictedDependencies.Contracts`** — the netstandard2.0 types this project and the analyzer share: the document format itself. The analyzer-config keys, diagnostic ids and observation property names join it with the analyzer.

See [docs/diagnostics.md](https://github.com/bitwarden/dotnet-extensions/blob/main/docs/diagnostics.md) for the diagnostics once the analyzer lands.
