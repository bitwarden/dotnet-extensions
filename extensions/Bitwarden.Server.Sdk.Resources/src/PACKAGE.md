# Bitwarden.Server.Sdk.Resources

**Audience:** engineers writing the API of a Bitwarden service: its resources and the operations on
them.

## About

The API conventions Bitwarden's .NET services share. You declare a resource as a C# record and map
handlers as ASP.NET Core minimal APIs with one standard attribute each, and the package gives you
the documents, binding and validation, the error document, querying, bulk operations and the
OpenAPI description of all of these.

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

## Configuration

The package reads no settings of its own. `[SelfHostedOnly]` and `[NotSelfHostedOnly]` follow
`IBitwardenEnvironment.SelfHosted`, which comes from `globalSettings:selfHosted`.

## Key concepts

- **A resource and its document.** `Resource`, `ResourceDocument<T>` and the list documents wrap and
  unwrap the envelope (`data`, `attributes`, `meta`), so a handler takes and returns plain records.
  See [Payload shape](https://contributing.bitwarden.com/contributing/server-sdk/payloads-and-responses/payload-shape).
- **Standard operations.** `[StandardCreate]`, `[StandardReadOne]` and the rest, applied by
  `WithStandards()`, bind and validate the request before the handler runs, set the status and
  `Location`, name the operation and settle its authorization. See
  [Verbs](https://contributing.bitwarden.com/contributing/server-sdk/operations/verbs).
- **Startup checks.** A handler mapped against its attribute or an operation identifier used twice
  stops the service at startup, every problem listed at once. See
  [Keep the Map line and the attribute in step](https://contributing.bitwarden.com/contributing/server-sdk/operations/verbs#keep-the-map-line-and-the-attribute-in-step).
- **The standard error document** is written for every refusal and every exception. See
  [Errors](https://contributing.bitwarden.com/contributing/server-sdk/payloads-and-responses/errors).
- **Zero knowledge.** Bitwarden services store and synchronize Vault Data only as Protected Data,
  encrypted on the client. `[Encrypted]` checks an encrypted value's length and, if asked, its
  shape. Nothing in the package decrypts or logs one.

## Getting started

A resource and the endpoints that create and read it, in `WidgetsEndpoints.cs`:

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

## Set up a host without Service

In `Program.cs`:

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
  [OpenAPI](https://contributing.bitwarden.com/contributing/server-sdk/contracts/openapi#turn-on-the-document).

## Gotchas and constraints

- **A handler that takes a bare `Query` parameter**, on any endpoint, stops the service at startup:
  its filter would reach the handler unchecked. Take a `Search<T>` on a `[StandardSearch]`
  operation, or the bulk request on its `[StandardBulk...]` operation.
- **The package doesn't enforce every API standard.** It accepts some requests the standards refuse,
  and its startup checks miss some mistakes.
  [Known limitations](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/src/README.md#known-limitations)
  lists each, and the developer guide gives the workaround where there is one.

## Developer guide

The [developer guide](https://contributing.bitwarden.com/contributing/server-sdk/),
in Bitwarden.Server.Sdk.Service, covers this package one topic per page.

**Running a service**

- [Keep an operation to one kind of install](https://contributing.bitwarden.com/contributing/server-sdk/running-a-service/configuration-and-self-hosting#keep-an-operation-to-one-kind-of-install): `[SelfHostedOnly]` and `[NotSelfHostedOnly]`.
- [Add to the standard endpoints from a package](https://contributing.bitwarden.com/contributing/server-sdk/running-a-service/caching-and-extensions#add-to-the-standard-endpoints-from-a-package): `IStandardEndpointFilterProvider`, `IStandardEndpointCheck` and `ServedByPlatform<T>()`.

**Contracts**

- [OpenAPI](https://contributing.bitwarden.com/contributing/server-sdk/contracts/openapi): the generated document, operation identifiers and committing the contract.
- [API paths](https://contributing.bitwarden.com/contributing/server-sdk/contracts/api-paths): collection and resource paths, and checking your own at startup.
- [Breaking changes and versioning](https://contributing.bitwarden.com/contributing/server-sdk/contracts/breaking-changes-and-versioning)
- [Unrecognized fields, query parameters, and headers](https://contributing.bitwarden.com/contributing/server-sdk/contracts/unrecognized-fields-query-parameters-and-headers)
- [Deprecation](https://contributing.bitwarden.com/contributing/server-sdk/contracts/deprecation)

**Payloads and responses**

- [Payload shape](https://contributing.bitwarden.com/contributing/server-sdk/payloads-and-responses/payload-shape): resources, the document envelope and resource fields.
- [JSON](https://contributing.bitwarden.com/contributing/server-sdk/payloads-and-responses/json): dates, enumerations and free-form values.
- [Naming conventions](https://contributing.bitwarden.com/contributing/server-sdk/payloads-and-responses/naming-conventions)
- [Standard fields](https://contributing.bitwarden.com/contributing/server-sdk/payloads-and-responses/standard-fields): `id`, `type`, `version` and the audit fields.
- [Content negotiation](https://contributing.bitwarden.com/contributing/server-sdk/payloads-and-responses/content-negotiation)
- [Standard responses](https://contributing.bitwarden.com/contributing/server-sdk/payloads-and-responses/standard-responses): the response helpers and status codes.
- [Request validation](https://contributing.bitwarden.com/contributing/server-sdk/payloads-and-responses/request-validation)
- [Errors](https://contributing.bitwarden.com/contributing/server-sdk/payloads-and-responses/errors): the error document and the exceptions that produce it.

**Operations**

- [Verbs](https://contributing.bitwarden.com/contributing/server-sdk/operations/verbs): the standard attributes and how routes are checked.
- [Creating resources](https://contributing.bitwarden.com/contributing/server-sdk/operations/creating-resources)
- [Reading resources](https://contributing.bitwarden.com/contributing/server-sdk/operations/reading-resources)
- [Updating resources](https://contributing.bitwarden.com/contributing/server-sdk/operations/updating-resources)
- [Partial updates](https://contributing.bitwarden.com/contributing/server-sdk/operations/partial-updates)
- [Optimistic concurrency](https://contributing.bitwarden.com/contributing/server-sdk/operations/optimistic-concurrency)
- [Deleting resources](https://contributing.bitwarden.com/contributing/server-sdk/operations/deleting-resources), including soft deletes.
- [Acting upon resources](https://contributing.bitwarden.com/contributing/server-sdk/operations/acting-upon-resources)

**Querying**

- [Filtering](https://contributing.bitwarden.com/contributing/server-sdk/querying/filtering), including multi-value parameters and ranges.
- [Paging](https://contributing.bitwarden.com/contributing/server-sdk/querying/paging): offset and cursor paging.
- [Sorting](https://contributing.bitwarden.com/contributing/server-sdk/querying/sorting)
- [Sparse fieldsets](https://contributing.bitwarden.com/contributing/server-sdk/querying/sparse-fieldsets)
- [Advanced queries](https://contributing.bitwarden.com/contributing/server-sdk/querying/advanced-queries): searches with filter expressions.

**Bulk and asynchronous work**

- [Bulk operations](https://contributing.bitwarden.com/contributing/server-sdk/bulk-and-asynchronous-work/bulk-operations/)
    - [Targeting](https://contributing.bitwarden.com/contributing/server-sdk/bulk-and-asynchronous-work/bulk-operations/targeting)
    - [Per-resource rules](https://contributing.bitwarden.com/contributing/server-sdk/bulk-and-asynchronous-work/bulk-operations/per-resource-rules)
    - [All-or-nothing and best-effort](https://contributing.bitwarden.com/contributing/server-sdk/bulk-and-asynchronous-work/bulk-operations/all-or-nothing-and-best-effort)
    - [Synchronous and asynchronous](https://contributing.bitwarden.com/contributing/server-sdk/bulk-and-asynchronous-work/bulk-operations/synchronous-and-asynchronous)
    - [Bulk responses](https://contributing.bitwarden.com/contributing/server-sdk/bulk-and-asynchronous-work/bulk-operations/bulk-responses)
    - [Bulk creates](https://contributing.bitwarden.com/contributing/server-sdk/bulk-and-asynchronous-work/bulk-operations/bulk-creates)
    - [Bulk updates](https://contributing.bitwarden.com/contributing/server-sdk/bulk-and-asynchronous-work/bulk-operations/bulk-updates)
    - [Bulk replaces](https://contributing.bitwarden.com/contributing/server-sdk/bulk-and-asynchronous-work/bulk-operations/bulk-replaces)
    - [Bulk deletes](https://contributing.bitwarden.com/contributing/server-sdk/bulk-and-asynchronous-work/bulk-operations/bulk-deletes)
    - [Bulk actions](https://contributing.bitwarden.com/contributing/server-sdk/bulk-and-asynchronous-work/bulk-operations/bulk-actions)
- [Jobs](https://contributing.bitwarden.com/contributing/server-sdk/bulk-and-asynchronous-work/jobs)
