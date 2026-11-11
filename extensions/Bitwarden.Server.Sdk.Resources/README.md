# Bitwarden.Server.Sdk.Resources

Declare a resource and a handler, and get a consistent API: the request bound and checked, the
response and its errors shaped the same way in every service, and an OpenAPI document that says so.

`Bitwarden.Server.Sdk.Resources` holds what Bitwarden's .NET services share about their APIs. You
write a resource as a C# record and a handler as an ordinary ASP.NET Core minimal API with one
standard attribute, and the package does the rest:

- **Resource documents.** `Resource`, `ResourceDocument<T>` and the list documents wrap and unwrap
  the envelope, so a handler takes and returns plain records.
- **Standard endpoints.** `[StandardCreate]`, `[StandardReadOne]`, `[StandardUpdate]` and the others,
  applied by `WithStandards()`, bind and validate the request before the handler runs, set the status
  code and `Location`, name the operation and settle its authorization.
- **Standard errors.** Exceptions such as `NotFoundException` and every refusal the package makes
  become the same error document, with the status, a code and where the problem is.
- **Querying.** Filters, sorting, offset and cursor paging, sparse fieldsets, searches and bulk
  operations, each an ordinary handler parameter.
- **The OpenAPI description.** Resources in their envelope, the standard responses and errors,
  paging, filtering and sorting parameters, and sections named for a reader.
- **Zero knowledge.** A service stores and passes on Vault Data only as Protected Data, encrypted on
  the client. `[Encrypted]` checks an encrypted value's length and, if asked, its shape. Nothing in
  the package decrypts or logs it.

Mistakes are caught early. A handler whose route disagrees with its attribute, an operation
identifier used twice or an authorization that contradicts itself stops the service at startup,
with every problem listed at once.

A service references `Bitwarden.Server.Sdk.Service`, which brings this package and wires it in. See
[Using it without Service](#use-it-without-service) for a host that doesn't.

## Getting started

Create a web project and add Service, which brings this package with it:

```bash
dotnet new web -n WidgetService
cd WidgetService
dotnet add package Bitwarden.Server.Sdk.Service --prerelease
```

Turn on the request delegate generator and XML documentation in `WidgetService.csproj`. The
generator compiles your handlers ahead of time, and the XML comments become the OpenAPI document's
summaries:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <EnableRequestDelegateGenerator>true</EnableRequestDelegateGenerator>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);Microsoft.AspNetCore.OpenApi.Generated</InterceptorsNamespaces>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <NoWarn>$(NoWarn);CS1591</NoWarn>
</PropertyGroup>
```

Replace `Program.cs` with a service, one resource and two operations:

```csharp
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
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
        // A widget belongs to a person, not to an organization.
        Organization = ContextRequirement.Optional,
    };

    protected override void ConfigureServices(IServiceCollection services)
    {
        // Lets your XML comments reach the OpenAPI document.
        services.AddOpenApi();
        services.AddSingleton<WidgetStore>();
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

// Keeps widgets in memory. A real service keeps them in its database.
public sealed class WidgetStore
{
    private readonly ConcurrentDictionary<string, Widget> _widgets = new();

    public Widget Add(Widget widget)
    {
        var stored = widget with { Id = Guid.NewGuid().ToString(), CreatedAt = DateTimeOffset.UtcNow };
        _widgets[stored.Id] = stored;
        return stored;
    }

    public Widget? Find(string id) => _widgets.GetValueOrDefault(id);
}
```

Run it:

```bash
dotnet run
```

- The OpenAPI document at `/api/openapi.json` lists `createWidget` and `getWidget` under a "Widgets"
  section, with the `Widget` resource in its envelope. The documentation UI is at `/docs`.
- `POST /api/v1/widgets` answers `401` until you send a bearer token from the Bitwarden Identity
  service that the service is configured to trust. See Service's
  [Authentication and authorization](../Bitwarden.Server.Sdk.Service/docs/running-a-service/authentication-and-authorization.md).

With a token, `POST /api/v1/widgets` creates a widget and answers `201` with a `Location` header. The
request is checked before your handler runs: a name longer than 100 characters is refused `422`, and
the error says which field is wrong. `GET /api/v1/widgets/{id}` for a widget that doesn't exist
answers `404` in the same error document.

## Examples

### List a collection with filters, sorting and paging

Each query parameter is an ordinary handler parameter, so the signature is the whole request. The
caller writes `GET /api/v1/widgets?filter[createdAt]=[2026-01-01T00:00:00Z,*)&sort=-createdAt`:

```csharp
using System.ComponentModel.DataAnnotations;
using Bitwarden.Server.Sdk.Resources;
using Bitwarden.Server.Sdk.Resources.Querying;
using Microsoft.EntityFrameworkCore;
using static Bitwarden.Server.Sdk.Resources.StandardResponses;

[Sortable(nameof(CreatedAt), Default = true, Descending = true)]
[Filterable(nameof(CreatedAt))]
public sealed record Widget : Resource
{
    [Sortable]
    [StringLength(100)]
    public required string Name { get; init; }
}

public static class WidgetsEndpoints
{
    /// <summary>
    /// List widgets
    /// </summary>
    /// <param name="createdAt">Only widgets created within this range.</param>
    /// <param name="sort">The order of the list.</param>
    /// <param name="database">Where widgets are kept.</param>
    /// <param name="pageNumber">Which page to return.</param>
    /// <param name="pageSize">How many widgets a page holds.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    [StandardReadMany]
    public static async Task<PagedResourceListDocument<Widget>> ListWidgets(
        Filter<Range<DateTimeOffset>>? createdAt,
        Sort? sort,
        WidgetDatabase database,
        PageNumber pageNumber = default,
        PageSize pageSize = default,
        CancellationToken cancellationToken = default)
    {
        var page = new Page { Number = pageNumber, Size = pageSize };
        var widgets = database.Widgets.AsNoTracking().Within(widget => widget.CreatedAt, createdAt?.Value);

        var rows = await widgets
            .SortedBy(sort ?? Sort.Default<Widget>(), widget => widget.Id)
            .Page(page)
            .ToListAsync(cancellationToken);

        return ReadManyResponse(await rows.ToPagedAsync(page, ToResource, widgets.CountAsync, cancellationToken), page);
    }

    private static Widget ToResource(WidgetEntity entity) =>
        new() { Id = entity.Id.ToString(), Name = entity.Name, CreatedAt = entity.CreatedAt };
}
```

A sort field the resource doesn't mark `[Sortable]`, or a range that doesn't parse, is refused
`422`. A page size above 200 is reduced to 200, and the response's links carry the caller's filters
and sort to the next page. See [Filtering](docs/querying/filtering.md),
[Sorting](docs/querying/sorting.md) and [Paging](docs/querying/paging.md).

### Report a failure

A handler reports a failure by throwing one of the standard exceptions from
`Bitwarden.Server.Sdk.Errors`, and the package writes the error document:

```csharp
using Bitwarden.Server.Sdk.Errors;
using Bitwarden.Server.Sdk.Resources;
using static Bitwarden.Server.Sdk.Resources.StandardResponses;

public static class WidgetActions
{
    /// <summary>
    /// Archive a widget
    /// </summary>
    [StandardAction]
    public static async Task<ResourceDocument<Widget>> Archive(
        string id, IWidgetStore store, CancellationToken cancellationToken)
    {
        var widget = await store.FindAsync(id, cancellationToken) ?? throw new NotFoundException("widget", id);

        if (widget.Archived)
        {
            throw new InvalidRequestException("The widget is already archived.", code: "invalid-state");
        }

        return ActionResponse(await store.ArchiveAsync(widget, cancellationToken));
    }
}
```

The caller gets `404` for a widget that doesn't exist and `422` for one already archived, each with
the code and title in the error document. Any other exception becomes a `500` that describes
nothing, logged against the identifier the response carries. See
[Errors](docs/payloads-and-responses/errors.md).

### Use it without Service

`Service` makes every call below for you except `WithStandards()`, which each group of handlers
makes. A host built without `Service` makes them all itself:

```csharp
using Bitwarden.Server.Sdk.Resources;
using Bitwarden.Server.Sdk.Resources.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Exceptions become the standard error document. UseExceptionHandler needs problem details.
builder.Services.AddStandardErrors();
builder.Services.AddProblemDetails();

// What WithStandards() reads while endpoints are built, and how their operations are described.
builder.Services.AddStandardEndpoints();
builder.Services.AddOpenApi(options => options.DescribeResources());

var app = builder.Build();

app.UseExceptionHandler();
app.UseStandardStatusCodePages();

app.MapGroup("api/v1/widgets").WithStandards();
app.MapOpenApi();

// Once every endpoint is mapped: stops the host if any of them breaks a standard.
app.RequireStandards();

app.Run();
```

| Entry point                        | Where                                      | What it does                                                                             |
| ---------------------------------- | ------------------------------------------ | ---------------------------------------------------------------------------------------- |
| `services.AddStandardErrors()`     | `Microsoft.Extensions.DependencyInjection` | Turns any exception reaching the pipeline into the standard error document.              |
| `app.UseStandardStatusCodePages()` | `Bitwarden.Server.Sdk.Resources`           | Answers a failure status with no body, such as a routing `404`, with the error document. |
| `services.AddStandardEndpoints()`  | `Microsoft.Extensions.DependencyInjection` | Registers what standard endpoints need while they are built and described.               |
| `builder.WithStandards()`          | `Bitwarden.Server.Sdk.Resources`           | Applies the standards to every endpoint a group or endpoint builder maps.                |
| `app.RequireStandards()`           | `Bitwarden.Server.Sdk.Resources`           | Refuses to start a host whose endpoints break a standard, naming every problem.          |
| `options.DescribeResources()`      | `Bitwarden.Server.Sdk.Resources.OpenApi`   | Describes resources and standard operations in the OpenAPI document.                     |

Such a host also needs what the package relies on and `Service` sets up:

- **Authentication and caller authorization.** Every standard operation carries an authorization
  requirement. See `AddCallerAuthorization` in Bitwarden.Server.Sdk.Authorization.
- **The environment.** `[SelfHostedOnly]` and `[NotSelfHostedOnly]` read `IBitwardenEnvironment`
  from Bitwarden.Server.Sdk.Environment.
- **The serializer settings.** Documents are written with the host's JSON options. Without the ones
  `Service` applies, the error document carries members such as `"id": null` that a service leaves
  out.

That is why a service references `Service` rather than this package.

## Developer guide

The [developer guide](docs/README.md) covers everything else, one topic per page, each with code
you can copy:

- [Contracts](docs/README.md#contracts): OpenAPI, API paths, versioning, unrecognized input,
  deprecation.
- [Payloads and responses](docs/README.md#payloads-and-responses): resources, JSON, naming, standard
  fields, content negotiation, responses, validation, errors.
- [Operations](docs/README.md#operations): create, read, update, partial updates, optimistic
  concurrency, delete, actions.
- [Querying](docs/README.md#querying): filtering, paging, sorting, sparse fieldsets, searches.
- [Bulk and asynchronous work](docs/README.md#bulk-and-asynchronous-work): bulk operations and
  jobs.

Running a service (defining it, authentication, configuration, telemetry) is in Service's
[developer guide](../Bitwarden.Server.Sdk.Service/docs/README.md).

## Configuration

The package reads no settings of its own. `[SelfHostedOnly]` and `[NotSelfHostedOnly]` follow
`IBitwardenEnvironment.SelfHosted`, which comes from `globalSettings:selfHosted`.

## Requirements

- .NET 10 and ASP.NET Core.
- The package references Bitwarden.Server.Sdk.Authorization, Bitwarden.Server.Sdk.Context,
  Bitwarden.Server.Sdk.Data.Abstractions, Bitwarden.Server.Sdk.Errors,
  Bitwarden.Server.Sdk.Environment and Microsoft.AspNetCore.OpenApi.

## Contributing

To work on the package itself, see [Contributing to Bitwarden.Server.Sdk.Resources](docs/contributing.md):
how to run its tests, and the rules a change has to keep. [Open issues](OPEN-ISSUES.md) lists the
package's known gaps.
