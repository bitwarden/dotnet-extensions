# Bitwarden.Server.Sdk.Features.Analyzers

Roslyn analyzer and incremental source generator for the Bitwarden feature flag system.

## Contents

- **`FeatureFlagAnalyzer`** – Diagnostic analyzer that enforces feature flag hygiene on classes
  annotated with `[FlagKeyCollection]`.
- **`FeaturesGenerator`** – Incremental source generator that emits a `GetKeys()` method on each
  `[FlagKeyCollection]` class.

## Diagnostics

| ID | Severity | Title |
|----|----------|-------|
| [BW0001](https://github.com/bitwarden/dotnet-extensions/blob/main/docs/diagnostics.md#bw0001) | Info | Feature flags should be removed once not used |
| [BW0002](https://github.com/bitwarden/dotnet-extensions/blob/main/docs/diagnostics.md#bw0002) | Warning | Flag key value should not be null or empty |

## Source Generator

`FeaturesGenerator` runs on every class decorated with `[FlagKeyCollection]` and generates a
`partial` class containing:

```csharp
public static IReadOnlyCollection<string> GetKeys()
{
    return [Field1, Field2, ...];
}
```

`GetKeys()` returns the names (not values) of every `const string` field declared directly on the
type. The generated file is named `FlagKeyCollection.<TypeName>.g.cs`.

## Running Tests

Analyzer tests use `xunit.v3.mtp-v2` and must be run with `dotnet run` on the .NET 10 SDK:

```bash
dotnet run --project tests/Bitwarden.Server.Sdk.Features.Analyzers.Tests
dotnet run --project tests/Bitwarden.Server.Sdk.Features.CodeFixers.Tests
```
