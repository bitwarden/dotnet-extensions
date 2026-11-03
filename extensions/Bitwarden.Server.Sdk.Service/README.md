# Bitwarden.Server.Sdk.Service

Build a Bitwarden service by writing only what is yours: its resources, its handlers and its data.

`Bitwarden.Server.Sdk.Service` is the hosting model for Bitwarden's .NET services. You derive one
class from `Service`, map your endpoints as ordinary ASP.NET Core minimal APIs, and the package does
the rest in a fixed, reviewed order:

- **Secure by default.** Every request is authenticated, token audiences are always checked, and
  each operation is authorized by scope and by the organization, user and provider it concerns.
- **Consistent APIs with little code.** Declare a resource and a handler, and the package binds and
  validates the request, shapes the response and its errors, and gives you filtering, sorting,
  paging, sparse fieldsets and bulk operations.
- **Documented as you write it.** An OpenAPI document and a documentation UI are generated from your
  handlers' signatures and XML comments.
- **Ready to run.** Traces, metrics and logs over OpenTelemetry, health probes, per-address rate
  limits, CORS, trusted proxies and feature flags.
- **Zero knowledge.** Bitwarden services store and pass on Vault Data only as Protected Data,
  encrypted on the client. Nothing in the package decrypts or inspects an encrypted value.

Mistakes are caught early. A handler whose route disagrees with its declared operation, an operation
identifier used twice or an authorization that contradicts itself stops the service at startup,
with every problem listed at once.

## Getting started

Create a web project and add the package:

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
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

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

- Open the service's address in a browser: `/` redirects to the documentation UI at `/docs`, and the
  OpenAPI document is at `/api/openapi.json`.
- `GET /api/v1/health` answers `200` with `{"status":"HEALTHY"}`.
- `POST /api/v1/widgets` answers `401` until you send a bearer token. Point the service at the
  Bitwarden Identity service that issues your tokens in `appsettings.json`:

```json
{
    "Authentication": {
        "Schemes": {
            "Bearer": {
                "Authority": "https://identity.example.com"
            }
        }
    }
}
```

With a token, `POST /api/v1/widgets` creates a widget and answers `201` with a `Location` header.
The request is validated before your handler runs: a name longer than 100 characters is refused
`422`, and the error says which field is wrong.

## Examples

### List a collection with filters, sorting and paging

Each query parameter is an ordinary handler parameter, so the signature is the whole request. The
caller writes `GET /api/v1/widgets?filter[createdAt]=[2026-01-01T00:00:00Z,*)&sort=-createdAt`:

```csharp
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.EntityFrameworkCore;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

[Sortable(nameof(CreatedAt), Default = true, Descending = true)]
[Filterable(nameof(CreatedAt))]
public sealed record Widget : Resource
{
    [Sortable]
    [StringLength(100)]
    public required string Name { get; init; }
}

/// <summary>
/// List widgets
/// </summary>
/// <param name="createdAt">Only widgets created within this range.</param>
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
```

A sort field the resource doesn't mark `[Sortable]`, or a range that doesn't parse, is refused
`422`. A page size above 200 is reduced to 200, and the response's links carry the caller's filters
and sort to the next page. See [Filtering](docs/querying/filtering.md),
[Sorting](docs/querying/sorting.md) and [Paging](docs/querying/paging.md).

### Require an organization and a scope for one operation

Authorization comes from the service's defaults, a handler class and the handler itself, the most
specific winning:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;

/// <summary>
/// Delete a widget the organization shares
/// </summary>
[Authorization(Scope = "widgets.admin", Organization = ContextRequirement.Required)]
[StandardDelete]
public static Task DeleteSharedWidget(string id, IContext context, IWidgetStore store, CancellationToken cancellationToken) =>
    store.DeleteAsync(context.OrganizationId!.Value, id, cancellationToken);
```

A caller without the scope, or naming no organization, is refused `403`, with an error saying
which requirement was not met, before the handler runs. See [Authentication and authorization](docs/running-a-service/authentication-and-authorization.md).

## Developer guide

The [developer guide](docs/README.md) covers everything else, one topic per page, each with code
you can copy:

- [Contracts](docs/README.md#contracts): OpenAPI, API paths, versioning, deprecation.
- [Payloads and responses](docs/README.md#payloads-and-responses): resources, JSON, standard
  fields, responses, validation, errors.
- [Operations](docs/README.md#operations): create, read, update, partial updates, optimistic
  concurrency, delete, actions.
- [Querying](docs/README.md#querying): filtering, paging, sorting, sparse fieldsets, searches.
- [Bulk and asynchronous work](docs/README.md#bulk-and-asynchronous-work): bulk operations and
  jobs.
- [Running a service](docs/README.md#running-a-service): defining a service,
  authentication and caller context, configuration and self-hosting, network limits, outbound HTTP,
  observability, caching and extensions.

## Requirements

- .NET 10.
- A Bitwarden Identity service to issue tokens, for anything beyond the anonymous system endpoints.
- The package references Bitwarden.Server.Sdk.Common, Bitwarden.Server.Sdk.Features,
  Bitwarden.Server.Sdk.HealthChecks and Bitwarden.Server.Sdk.Authentication. A service built on
  `Service` doesn't call `UseBitwardenSdk()` from the `Bitwarden.Server.Sdk` MSBuild SDK as well:
  `Service` already does what it does.

## Contributing

To work on the package itself, see [Contributing to Bitwarden.Server.Sdk.Service](docs/contributing.md):
how to run its tests, and the rules a change has to keep. [Open issues](OPEN-ISSUES.md) lists the
package's known gaps.
