# Bitwarden.Server.Sdk.Service

## About

The package a Bitwarden .NET service references. A service derives one class from `Service`, gives
it an identifier and a title, and maps its endpoints as minimal APIs. `Service` owns the rest of
startup in a fixed order, because the order of registration and middleware is security-relevant.

A service references this package alone. It brings these packages, and registers them in the order security needs:

| Package                                                                                                                                                                  | What it gives a service                                                              |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------ |
| [Resources](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/src/PACKAGE.md)                                           | Resource documents, the standard endpoints, querying and bulk operations.            |
| [Errors](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Errors/src/PACKAGE.md)                                                 | The error document and the exceptions that produce it.                               |
| [Authentication](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Authentication/src/PACKAGE.md)                                 | Bearer token validation.                                                             |
| [Authorization](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Authorization/src/PACKAGE.md)                                   | Scopes, context requirements, roles, trusted proxies and the call-chain check.       |
| [Context](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Context/src/PACKAGE.md)                                               | `IContext`, whom a request concerns, and the context headers.                        |
| [RateLimiting](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.RateLimiting/src/PACKAGE.md)                                     | Per-address request limits.                                                          |
| [Http](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Http/src/PACKAGE.md)                                                     | Outbound clients guarded against SSRF, and the internal TLS settings.                |
| [Idempotency](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Idempotency/src/PACKAGE.md)                                       | At-most-once writes from other services that send an `Idempotency-Key`.              |
| [Idempotency.Abstractions](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Idempotency.Abstractions/src/PACKAGE.md)             | The `Idempotency-Key` header, valid keys, and naming work so its retries reuse keys. |
| [Sagas](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Sagas/src/PACKAGE.md)                                                   | `[SpansServices]`, declaring how an operation whose writes span services recovers.   |
| [Data.Abstractions](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Data.Abstractions/src/PACKAGE.md)                           | `[Encrypted]`, `[PersonalData]`, `IRowOwnership` and `RequestMemo`.                  |
| [Resources.Caching.Abstractions](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources.Caching.Abstractions/src/PACKAGE.md) | The service-owned cache's key formats.                                               |
| [Service.Abstractions](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service.Abstractions/src/PACKAGE.md)                     | `IServiceExtension`, for a package that extends every service referencing it.        |
| [HealthChecks](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.HealthChecks/src/PACKAGE.md)                                     | Health checks and their tags.                                                        |
| [Features](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Features/src/PACKAGE.md)                                             | Feature flags: `IFeatureService` and `[RequireFeature]`.                             |

All are `Bitwarden.Server.Sdk.*`. It also brings ASP.NET Core's OpenAPI generator, an embedded
documentation UI and the OpenTelemetry exporter and instrumentation. `Service` itself keeps the base
class and its startup order, self-hosting, the system endpoints and health probes, telemetry, the
documentation UI, a product service's config resource, serving internal TLS, finishing writes whose
client left, the guard on operations whose writes span services, and `GetOrResolveOnceAsync`.

Bitwarden services are zero knowledge: they store and synchronize Vault Data only as Protected Data,
encrypted on the client. Nothing in this package decrypts an encrypted value. A resource declares an
encrypted member with `[Encrypted]` (Data.Abstractions).

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Service --prerelease
```

## Getting started

In a `Microsoft.NET.Sdk.Web` project, turn on the request delegate generator and XML documentation.
A handler documents only what a caller sends, not the services it takes, so `CS1573` is off as well
as `CS1591`:

```xml
<PropertyGroup>
  <EnableRequestDelegateGenerator>true</EnableRequestDelegateGenerator>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);Microsoft.AspNetCore.Http.Generated;Microsoft.AspNetCore.OpenApi.Generated</InterceptorsNamespaces>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <NoWarn>$(NoWarn);CS1591;CS1573</NoWarn>
</PropertyGroup>
```

Then `Program.cs` is a service, a resource and an operation. Types named `Widget...` are the
service's own:

```csharp
using Bitwarden.Server.Sdk.Authorization;
using Bitwarden.Server.Sdk.Data;
using Bitwarden.Server.Sdk.Errors;
using Bitwarden.Server.Sdk.Resources;
using Bitwarden.Server.Sdk.Service;
using static Bitwarden.Server.Sdk.Resources.StandardResponses;

new WidgetService(args).Run();

internal sealed class WidgetService(string[] args) : Service("widget-service", "Widget Service", args)
{
    protected override AuthorizationDefaults Authorization => new()
    {
        Organization = ContextRequirement.Optional,
        ReadScope = "widgets.read",
        WriteScope = "widgets.write",
    };

    protected override void ConfigureServices(IServiceCollection services)
    {
        // Lets your XML comments reach the OpenAPI document.
        services.AddOpenApi();
        services.AddSingleton<IWidgetStore, WidgetStore>();
    }

    protected override void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        WidgetsEndpoints.Map(endpoints);
}

/// <summary>
/// A widget.
/// </summary>
public sealed record Widget : Resource
{
    /// <summary>
    /// What the widget is called.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The person's notes, encrypted on their device. The service stores them as they arrive.
    /// </summary>
    [Encrypted(MaxLength = 10_000)]
    public string? Notes { get; init; }
}

[ApiTag(Description = "Widgets, which a person keeps.")]
public static class WidgetsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("api/v1/widgets").WithStandards().MapGet("{id}", GetWidget);

    /// <summary>
    /// Get a widget
    /// </summary>
    [StandardReadOne]
    public static async Task<ResourceDocument<Widget>> GetWidget(
        string id, IWidgetStore store, CancellationToken cancellationToken) =>
        ReadOneResponse(await store.FindAsync(id, cancellationToken)
            ?? throw new NotFoundException("widget", id));
}
```

Run it and open its address: `/` redirects to the documentation UI. Every operation but the
documentation and health probes answers `401` until the caller sends a token from the Identity
service named in `Authentication:Schemes:Bearer:Authority`.

## Writes that span services

`Service` notices an operation that writes to another service and anywhere else without declaring,
with `[SpansServices]` (Sagas), how it handles a failure part way. This check is the innermost of the
platform's middleware, inside idempotency keys, so a replayed answer isn't counted.

- It logs a warning and counts the operation in `bitwarden.operation.spans`. With
  `SpansServices:RefuseUndeclared` on (default `false`) it also refuses the write, as an unexpected
  failure. Turn it on in tests and Development.
- `[SpansServicesUnhandled]` admits a known defect: logged and counted as loudly, never refused.
- [Sagas](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Sagas/src/PACKAGE.md)
  describes the shapes to choose from.

## Idempotency keys

`Service` turns on
[Idempotency](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Idempotency/src/PACKAGE.md)
for every service, after authorization and the feature-flag checks, so a refusal is never recorded.
A write is done at most once only when its operation is marked `[AcceptsIdempotencyKey]`; Idempotency's
[Opt an operation in](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Idempotency/src/PACKAGE.md#opt-an-operation-in)
shows how, and its PACKAGE.md lists the `Idempotency:*` settings.

## Things to know

- **The documentation UI's token URL** is `{url}/connect/token` from `Service:PublicIdentityUrl`, or
  else from `Authentication:Schemes:Bearer:Authority`, `globalSettings:baseServiceUri:internalIdentity`
  or `globalSettings:baseServiceUri:identity`, the first set. With none, the OpenAPI document has no
  OAuth2 scheme.
- **`GetOrResolveOnceAsync` needs a cache without a `SizeLimit`.** Its entries have no size, so a
  size-limited `IMemoryCache` refuses them.

## Developer guide

The [developer guide](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/README.md)
covers writing a service with this package and Resources, one topic per page.

Running a service:

- [Defining a service](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/running-a-service/defining-a-service.md): the `Service` class, its hooks, the middleware order and the three kinds of service.
- [Authentication and authorization](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/running-a-service/authentication-and-authorization.md): scopes, context requirements, roles and token audiences.
- [Context](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/running-a-service/context.md): whom a call between services concerns, and how a service knows to believe it.
- [Configuration and self-hosting](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/running-a-service/configuration-and-self-hosting.md): settings, self-hosted installs, feature flags and the config resource.
- [Network and request limits](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/running-a-service/network-and-request-limits.md): trusted proxies, per-address rate limits and browser origins.
- [Outbound HTTP](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/running-a-service/outbound-http.md): calls guarded against SSRF, and internal TLS.
- [Observability and health](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/running-a-service/observability-and-health.md): traces, metrics, logs, re-entries and health probes.
- [Caching and extensions](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/running-a-service/caching-and-extensions.md): a short in-process memo, and extending the platform from a package.

Contracts:

- [OpenAPI](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/contracts/openapi.md): the generated document, the documentation UI and committing the contract.
- [API paths](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/contracts/api-paths.md)
- [Breaking changes and versioning](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/contracts/breaking-changes-and-versioning.md)
- [Unrecognized fields, query parameters, and headers](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/contracts/unrecognized-fields-query-parameters-and-headers.md)
- [Deprecation](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/contracts/deprecation.md)

Payloads and responses:

- [Payload shape](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/payload-shape.md)
- [JSON](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/json.md): dates, enumerations and free-form values.
- [Naming conventions](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/naming-conventions.md)
- [Standard fields](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/standard-fields.md)
- [Content negotiation](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/content-negotiation.md)
- [Standard responses](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/standard-responses.md)
- [Request validation](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/request-validation.md)
- [Errors](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/errors.md)

Operations:

- [Verbs](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/verbs.md)
- [Creating resources](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/creating-resources.md)
- [Reading resources](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/reading-resources.md)
- [Updating resources](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/updating-resources.md)
- [Partial updates](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/partial-updates.md)
- [Optimistic concurrency](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/optimistic-concurrency.md)
- [Deleting resources](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/deleting-resources.md)
- [Acting upon resources](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/acting-upon-resources.md)

Querying:

- [Filtering](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/querying/filtering.md)
- [Paging](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/querying/paging.md)
- [Sorting](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/querying/sorting.md)
- [Sparse fieldsets](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/querying/sparse-fieldsets.md)
- [Advanced queries](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/querying/advanced-queries.md)

Bulk and asynchronous work:

- [Bulk operations](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/bulk-and-asynchronous-work/bulk-operations/index.md)
    - [Targeting](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/bulk-and-asynchronous-work/bulk-operations/targeting.md)
    - [Per-resource rules](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/bulk-and-asynchronous-work/bulk-operations/per-resource-rules.md)
    - [All-or-nothing and best-effort](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/bulk-and-asynchronous-work/bulk-operations/all-or-nothing-and-best-effort.md)
    - [Synchronous and asynchronous](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/bulk-and-asynchronous-work/bulk-operations/synchronous-and-asynchronous.md)
    - [Bulk responses](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/bulk-and-asynchronous-work/bulk-operations/bulk-responses.md)
    - [Bulk creates](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/bulk-and-asynchronous-work/bulk-operations/bulk-creates.md)
    - [Bulk updates](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/bulk-and-asynchronous-work/bulk-operations/bulk-updates.md)
    - [Bulk replaces](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/bulk-and-asynchronous-work/bulk-operations/bulk-replaces.md)
    - [Bulk deletes](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/bulk-and-asynchronous-work/bulk-operations/bulk-deletes.md)
    - [Bulk actions](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/bulk-and-asynchronous-work/bulk-operations/bulk-actions.md)
- [Jobs](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/bulk-and-asynchronous-work/jobs.md)
