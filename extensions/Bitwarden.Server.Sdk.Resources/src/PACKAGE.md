# Bitwarden.Server.Sdk.Resources

## About

The API conventions Bitwarden's .NET services share. You declare a resource as a C# record and map
handlers as ASP.NET Core minimal APIs with one standard attribute each, and the package gives you:

- **Resource documents.** `Resource`, `ResourceDocument<T>` and the list documents wrap and unwrap
  the envelope (`data`, `attributes`, `meta`), so a handler takes and returns plain records.
- **Standard endpoints.** `[StandardCreate]`, `[StandardReadOne]` and the rest, applied by
  `WithStandards()`, bind and validate the request before the handler runs, set the status and
  `Location`, name the operation and settle its authorization.
- **The standard error document,** written for every refusal and every exception.
- **Querying:** filters, sorting, offset and cursor paging, sparse fieldsets, searches and bulk
  operations, each an ordinary handler parameter.
- **The OpenAPI description** of all of these.
- **Startup checks.** A handler mapped against its attribute or an operation identifier used twice
  stops the service at startup, every problem listed at once.

Bitwarden services are zero knowledge: they store and synchronize Vault Data only as Protected
Data, encrypted on the client. `[Encrypted]` checks an encrypted value's length and, if asked, its
shape. Nothing in the package decrypts or logs one.

The package references Bitwarden.Server.Sdk.Authorization, Bitwarden.Server.Sdk.Context,
Bitwarden.Server.Sdk.Data.Abstractions, Bitwarden.Server.Sdk.Errors, Bitwarden.Server.Sdk.Environment
and Microsoft.AspNetCore.OpenApi.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Resources
```

A service installs Bitwarden.Server.Sdk.Service instead, which brings this package and makes every
setup call but `WithStandards()`. A host without Service follows
[Set up a host without Service](#set-up-a-host-without-service).

## Getting started

A resource and the endpoints that create and read it:

```csharp
using System.ComponentModel.DataAnnotations;
using Bitwarden.Server.Sdk.Data;
using Bitwarden.Server.Sdk.Errors;
using Bitwarden.Server.Sdk.Resources;
using static Bitwarden.Server.Sdk.Resources.StandardResponses;

public sealed record Widget : Resource
{
    [StringLength(100)]
    public required string Name { get; init; }

    // Protected Data, encrypted on the person's device: stored as it arrives, never read.
    [Encrypted(MaxLength = 10_000)]
    public string? Notes { get; init; }
}

[ApiTag(Description = "Widgets, which a person keeps.")]
public static class WidgetsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup("api/v1/widgets").WithStandards();
        widgets.MapPost("", CreateWidget);
        widgets.MapGet("{id}", GetWidget);
    }

    /// <summary>
    /// Create a widget
    /// </summary>
    [StandardCreate]
    public static async Task<ResourceDocument<Widget>> CreateWidget(
        ResourceDocument<Widget> request, IWidgetStore store, CancellationToken cancellationToken) =>
        CreateResponse(await store.AddAsync(request.Data, cancellationToken));

    /// <summary>
    /// Get a widget
    /// </summary>
    [StandardReadOne]
    public static async Task<ResourceDocument<Widget>> GetWidget(
        string id, IWidgetStore store, CancellationToken cancellationToken) =>
        ReadOneResponse(await store.FindAsync(id, cancellationToken) ?? throw new NotFoundException("widget", id));
}
```

A name over 100 characters is refused `422` before the handler runs, a create answers `201` with
`Location`, and a widget that doesn't exist answers `404` in the standard error document. The
service calls `WidgetsEndpoints.Map` from its `MapEndpoints`, as in Service's
[Getting started](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/src/PACKAGE.md#getting-started).

## Configuration

The package reads no settings of its own. `[SelfHostedOnly]` and `[NotSelfHostedOnly]` follow
`IBitwardenEnvironment.SelfHosted`, which comes from `globalSettings:selfHosted`.

## Startup checks

Besides the problems the developer guide lists, a handler that takes a bare `Query` parameter, on
any endpoint, stops the service at startup: its filter would reach the handler unchecked. Take a
`Search<T>` on a `[StandardSearch]` operation, or the bulk request on its `[StandardBulk...]`
operation.

## Set up a host without Service

```csharp
using Bitwarden.Server.Sdk.Resources;
using Bitwarden.Server.Sdk.Resources.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddStandardErrors();
builder.Services.AddProblemDetails();
builder.Services.AddStandardEndpoints();
builder.Services.AddOpenApi(options => options.DescribeResources());

var app = builder.Build();

app.UseExceptionHandler();
app.UseStandardStatusCodePages();
WidgetsEndpoints.Map(app);
app.MapOpenApi();

// Once every endpoint is mapped: stops the host if any of them breaks a standard.
app.RequireStandards();

app.Run();
```

- `AddStandardErrors` turns any exception into the error document, and `UseStandardStatusCodePages`
  answers a bare failure status, such as a routing `404`, with it.
- Such a host also needs what Service sets up: authentication and caller authorization (every
  standard operation carries an authorization requirement), `IBitwardenEnvironment`, and Service's
  serializer settings. Documents are written with the host's JSON options, so without Service's an
  error document carries members such as `"id": null`.
- The project file settings that let handlers compile ahead of time and their XML comments reach
  the document are in
  [OpenAPI](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/contracts/openapi.md#turn-on-the-document).

## Developer guide

The [developer guide](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/README.md),
in Bitwarden.Server.Sdk.Service, covers this package one topic per page.

**Running a service**

- [Keep an operation to one kind of install](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/running-a-service/configuration-and-self-hosting.md#keep-an-operation-to-one-kind-of-install): `[SelfHostedOnly]` and `[NotSelfHostedOnly]`.
- [Add to the standard endpoints from a package](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/running-a-service/caching-and-extensions.md#add-to-the-standard-endpoints-from-a-package): `IStandardEndpointFilterProvider`, `IStandardEndpointCheck` and `ServedByPlatform<T>()`.

**Contracts**

- [OpenAPI](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/contracts/openapi.md): the generated document, operation identifiers and committing the contract.
- [API paths](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/contracts/api-paths.md): collection and resource paths, and checking your own at startup.
- [Breaking changes and versioning](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/contracts/breaking-changes-and-versioning.md)
- [Unrecognized fields, query parameters, and headers](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/contracts/unrecognized-fields-query-parameters-and-headers.md)
- [Deprecation](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/contracts/deprecation.md)

**Payloads and responses**

- [Payload shape](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/payload-shape.md): resources, the document envelope and resource fields.
- [JSON](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/json.md): dates, enumerations and free-form values.
- [Naming conventions](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/naming-conventions.md)
- [Standard fields](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/standard-fields.md): `id`, `type`, `version` and the audit fields.
- [Content negotiation](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/content-negotiation.md)
- [Standard responses](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/standard-responses.md): the response helpers and status codes.
- [Request validation](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/request-validation.md)
- [Errors](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/payloads-and-responses/errors.md): the error document and the exceptions that produce it.

**Operations**

- [Verbs](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/verbs.md): the standard attributes and how routes are checked.
- [Creating resources](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/creating-resources.md)
- [Reading resources](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/reading-resources.md)
- [Updating resources](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/updating-resources.md)
- [Partial updates](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/partial-updates.md)
- [Optimistic concurrency](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/optimistic-concurrency.md)
- [Deleting resources](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/deleting-resources.md), including soft deletes.
- [Acting upon resources](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/operations/acting-upon-resources.md)

**Querying**

- [Filtering](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/querying/filtering.md), including multi-value parameters and ranges.
- [Paging](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/querying/paging.md): offset and cursor paging.
- [Sorting](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/querying/sorting.md)
- [Sparse fieldsets](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/querying/sparse-fieldsets.md)
- [Advanced queries](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/querying/advanced-queries.md): searches with filter expressions.

**Bulk and asynchronous work**

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
