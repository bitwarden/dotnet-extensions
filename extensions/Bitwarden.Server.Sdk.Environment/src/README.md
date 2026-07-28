# Bitwarden.Server.Sdk.Environment

Provides runtime environment information for Bitwarden server services.

## Overview

This package exposes `IBitwardenEnvironment`, a singleton service that surfaces:

- **Version** — the application's version string, parsed from `AssemblyInformationalVersionAttribute`
- **GitHash** — the Git commit hash embedded in the informational version (the `+{hash}` suffix)
- **SelfHosted** / **SelfHostFlavor** — whether the instance is self-hosted and its deployment flavor (e.g., `"lite"`)

## Architecture

| Type | Role |
|------|------|
| `IBitwardenEnvironment` | Public interface injected by consumers |
| `RuntimeBitwardenEnvironment` | Internal singleton implementation |
| `IVersionInfoAccessor` | Public interface; provides access to parsed version information |
| `VersionInfoAccessor` | Internal; reads and caches `AssemblyInformationalVersionAttribute` |
| `VersionInfo` | Public; parses `{version}+{gitHash}` strings via `ISpanParsable<T>` |
| `SelfHostDetails` | Options class configured by the host application |

## Version Parsing

`VersionInfo` parses the informational version string produced by the .NET SDK when `<SourceRevisionId>`
or `<EmbedUntrackedSources>` are enabled. Expected format: `{SemVer}+{hex-hash}` (e.g., `1.2.3+af18b2952b5ddf910bd2f729a7c89a04b8d67084`).

A plain version string without a `+` suffix is also accepted (Git hash will be `null`).

## Testing

Run tests with `dotnet run`:

```bash
cd tests/Bitwarden.Server.Sdk.Environment.Tests
dotnet run
```
