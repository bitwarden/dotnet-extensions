# Bitwarden.Server.Sdk.WebEssentials

Shared web middleware and endpoint helpers for Bitwarden ASP.NET Core services.

## Installation

```shell
dotnet add package Bitwarden.Server.Sdk.WebEssentials
```

## Setup

Register services in `Program.cs`:

```csharp
builder.Services.AddWebEssentials();
```

## Security headers

Call `UseSecurityHeaders()` early in your middleware pipeline to append common browser-security
headers to every HTTP response:

```csharp
app.UseSecurityHeaders();
```

This adds the following headers to all responses:

| Header | Value | Purpose |
| --- | --- | --- |
| `x-frame-options` | `SAMEORIGIN` | Prevents the page from being embedded in a cross-origin frame |
| `x-xss-protection` | `1; mode=block` | Instructs legacy browsers to block reflected XSS attacks |
| `x-content-type-options` | `nosniff` | Prevents MIME-type sniffing |

## Version endpoint

Call `MapVersionEndpoint()` to register a `GET /version` route that returns the application's
version as JSON:

```csharp
app.MapVersionEndpoint();
```

Example response:

```json
{ "version": "1.2.3" }
```

The version is read from the application assembly's `AssemblyInformationalVersion`.
Build metadata (e.g. the `+<git-hash>` suffix appended by the .NET SDK) is stripped automatically.
