# Bitwarden.Server.Sdk.WebEssentials

Shared web middleware and endpoint helpers for Bitwarden ASP.NET Core services.

## Overview

This package provides two features:

- **Security headers middleware** — appends common browser-security HTTP headers to every response.
- **Version endpoint** — maps a `GET /version` route that returns the application's `AssemblyInformationalVersion` as JSON.

## Project structure

```text
src/                                        # Library
  WebEssentialsServiceCollectionExtensions  # AddWebEssentials()
  WebEssentialsApplicationBuilderExtensions # UseSecurityHeaders()
  WebEssentialsEndpointRouteBuilderExtensions # MapVersionEndpoint()
  SecurityHeadersMiddleware                 # Implementation
  VersionResponse                           # Response DTO
  WebEssentialsJsonSerializerContext        # Source-generated JSON context

tests/
  Bitwarden.Server.Sdk.WebEssentials.Tests         # Integration tests
```

## Security headers

`SecurityHeadersMiddleware` appends three headers to every response:

| Header | Value |
| --- | --- |
| `x-frame-options` | `SAMEORIGIN` |
| `x-xss-protection` | `1; mode=block` |
| `x-content-type-options` | `nosniff` |

The values are stored as static `StringValues` fields to avoid per-request allocation.

## Version endpoint

`MapVersionEndpoint()` registers a `GET /version` route. The version is resolved at registration
time from `IBitwardenEnvironment`, which is automatically registered by `AddWebEssentials()`.
`MapVersionEndpoint()` requires `AddWebEssentials()` to have been called first.

## Adding or changing features

- **New middleware**: add a class in `src/`, register it via a new extension method in `WebEssentialsApplicationBuilderExtensions`.
- **New endpoints**: add an extension method in `WebEssentialsEndpointRouteBuilderExtensions`. If
  the endpoint returns a new response type, add it to `WebEssentialsJsonSerializerContext` and
  register the context with `ConfigureHttpJsonOptions` in `WebEssentialsServiceCollectionExtensions`.
