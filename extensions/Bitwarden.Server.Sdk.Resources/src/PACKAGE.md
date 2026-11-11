# Bitwarden.Server.Sdk.Resources

## About

The API conventions Bitwarden's .NET services share. A service declares a resource as a C# record
and maps handlers as ASP.NET Core minimal APIs with one standard attribute each. The package gives
it:

- resource documents (`Resource`, `ResourceDocument<T>` and the list documents) that wrap and unwrap
  the envelope;
- standard endpoints (`[StandardCreate]`, `[StandardReadOne]` and the rest, applied by
  `WithStandards()`) that bind and validate the request, set the status and `Location`, name the
  operation and settle its authorization, with a startup check that refuses a service whose
  endpoints break a standard;
- the standard error document, written for every refusal and every exception;
- querying: filters, sorting, offset and cursor paging, sparse fieldsets, searches and bulk
  operations;
- the description of all of these in the service's OpenAPI document.

A host turns the package on with `services.AddStandardErrors()`, `services.AddStandardEndpoints()`,
`app.UseStandardStatusCodePages()`, `WithStandards()` on its endpoint groups, `app.RequireStandards()`
and `options.DescribeResources()` on its OpenAPI options. `Bitwarden.Server.Sdk.Service` makes every
one of these calls but `WithStandards()`, which each group of handlers makes, so a service normally
references only Service.

Bitwarden services are zero knowledge: they store and synchronize Vault Data only as Protected Data,
encrypted on the client. `[Encrypted]` checks an encrypted value's length and, with
`WellFormed = true`, its shape. Nothing in this package decrypts an encrypted value.

The package references Bitwarden.Server.Sdk.Authorization, Bitwarden.Server.Sdk.Context,
Bitwarden.Server.Sdk.Data.Abstractions, Bitwarden.Server.Sdk.Errors, Bitwarden.Server.Sdk.Environment
and Microsoft.AspNetCore.OpenApi.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Resources
```

A service installs `Bitwarden.Server.Sdk.Service` instead, which brings this package.

## Configuration

The package reads no settings of its own. `[SelfHostedOnly]` and `[NotSelfHostedOnly]` follow
`IBitwardenEnvironment.SelfHosted`, which comes from `globalSettings:selfHosted`.

## Getting started

A resource and the endpoints that create and read it:

```csharp
using System.ComponentModel.DataAnnotations;
using Bitwarden.Server.Sdk.Data;
using Bitwarden.Server.Sdk.Errors;
using Bitwarden.Server.Sdk.Resources;
using static Bitwarden.Server.Sdk.Resources.StandardResponses;

/// <summary>
/// A widget.
/// </summary>
public sealed record Widget : Resource
{
    /// <summary>
    /// What the widget is called.
    /// </summary>
    [StringLength(100)]
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
    public static ResourceDocument<Widget> CreateWidget(ResourceDocument<Widget> request, WidgetStore store) =>
        CreateResponse(store.Add(request.Data));

    /// <summary>
    /// Get a widget
    /// </summary>
    [StandardReadOne]
    public static ResourceDocument<Widget> GetWidget(string id, WidgetStore store) =>
        ReadOneResponse(store.Find(id) ?? throw new NotFoundException("widget", id));
}
```

A name over 100 characters is refused `422` before the handler runs, a create answers `201` with
`Location`, and a widget that doesn't exist answers `404` in the standard error document. The
[README](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/README.md#getting-started)
shows the project file and the service that maps these endpoints.

## Documentation

The [developer guide](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/README.md):

- **Contracts:** [OpenAPI](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/contracts/openapi.md),
  [API paths](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/contracts/api-paths.md),
  [Breaking changes and versioning](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/contracts/breaking-changes-and-versioning.md),
  [Unrecognized fields, query parameters, and headers](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/contracts/unrecognized-fields-query-parameters-and-headers.md),
  [Deprecation](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/contracts/deprecation.md).
- **Payloads and responses:** [Payload shape](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/payloads-and-responses/payload-shape.md),
  [JSON](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/payloads-and-responses/json.md),
  [Naming conventions](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/payloads-and-responses/naming-conventions.md),
  [Standard fields](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/payloads-and-responses/standard-fields.md),
  [Content negotiation](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/payloads-and-responses/content-negotiation.md),
  [Standard responses](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/payloads-and-responses/standard-responses.md),
  [Request validation](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/payloads-and-responses/request-validation.md),
  [Errors](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/payloads-and-responses/errors.md).
- **Operations:** [Verbs](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/operations/verbs.md),
  [Creating](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/operations/creating-resources.md),
  [Reading](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/operations/reading-resources.md),
  [Updating](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/operations/updating-resources.md),
  [Partial updates](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/operations/partial-updates.md),
  [Optimistic concurrency](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/operations/optimistic-concurrency.md),
  [Deleting](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/operations/deleting-resources.md),
  [Acting upon resources](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/operations/acting-upon-resources.md).
- **Querying:** [Filtering](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/querying/filtering.md),
  [Paging](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/querying/paging.md),
  [Sorting](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/querying/sorting.md),
  [Sparse fieldsets](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/querying/sparse-fieldsets.md),
  [Advanced queries](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/querying/advanced-queries.md).
- **Bulk and asynchronous work:** [Bulk operations](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/bulk-and-asynchronous-work/bulk-operations/index.md),
  [Jobs](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/docs/bulk-and-asynchronous-work/jobs.md).

Running a service is in [Bitwarden.Server.Sdk.Service's guide](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/README.md).
Known gaps are in [Open issues](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources/OPEN-ISSUES.md).
