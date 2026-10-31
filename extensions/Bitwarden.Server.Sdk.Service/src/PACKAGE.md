# Bitwarden.Server.Sdk.Service

## About

The hosting model for a Bitwarden .NET service. A service derives from `Service`, gives it an
identifier and a title, and maps its endpoints as minimal APIs. The base class owns the rest of
startup: the order of dependency registration and of middleware is security-relevant, so it is fixed
in `Service.Build()` rather than rediscovered by each service.

Every service gets, in a fixed order:

- authentication of every request by default, with token audiences always checked;
- authorization by scope and by the organization, user and provider a request concerns;
- the standard JSON:API request and response documents, binding, validation and error documents;
- querying (filters, sorting, offset and cursor paging, sparse fieldsets) and bulk operations;
- an OpenAPI document and a documentation UI;
- traces, metrics and logs over OTLP;
- per-address request limits, CORS for named browser origins, and health probes;
- feature flags, and for a product service a `GET /api/v1/config` resource.

Bitwarden services are zero knowledge: they store and synchronize Vault Data only as Protected Data,
encrypted on the client. Nothing in this package decrypts or inspects an encrypted value, and a
resource declares an encrypted member with `[Encrypted]` from Bitwarden.Server.Sdk.Common, which
checks its length only.

The package references Bitwarden.Server.Sdk.Common, Bitwarden.Server.Sdk.Features,
Bitwarden.Server.Sdk.HealthChecks and Bitwarden.Server.Sdk.Authentication.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Service
```

## Configuration

Every setting is optional. A service with none of them runs in the cloud configuration, with no
CORS, no trusted proxies, no rate limits and telemetry exported to the OTLP exporter's default
endpoint.

| Setting                                                              | Default                                       | What it does                                                                                                                                                           |
| -------------------------------------------------------------------- | --------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Bitwarden:ServiceName`                                              | The name passed to `Service`                  | The name the client packages authenticate as (`internal.{name}`). Supplied by this package as the lowest-precedence source, so a deployment can still set it.          |
| `Logging:Console:IncludeScopes`                                      | `true`                                        | Writes each request's log scope (its organization, user and provider) on console log lines. Supplied as a default; a deployment can turn it off.                       |
| `Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics`          | `Warning`                                     | Keeps ASP.NET Core's request lines, which carry the query string, out of the logs. Supplied as a default; set it lower only where a query never carries a credential.  |
| `globalSettings:selfHosted`                                          | `false`                                       | Marks a self-hosted install: layers in `appsettings.SelfHosted.json`, turns rate limits off and telemetry off by default, and sets `IBitwardenEnvironment.SelfHosted`. |
| `globalSettings:selfHostFlavor`                                      | `unknown`                                     | The self-hosted install's flavor, read only when `globalSettings:selfHosted` is true.                                                                                  |
| `Service:BrowserOrigins`                                             | None                                          | The browser origins admitted cross-origin, as an array or a single value. None means no CORS policy, middleware or headers.                                            |
| `Service:PublicIdentityUrl`                                          | None                                          | Where a browser reaches Identity, for the documentation UI's token request (`{value}/connect/token`). Unset, the token URL comes from the authority settings below.    |
| `Authentication:Schemes:Bearer:Authority`                            | None                                          | The bearer authority. Read here only to derive the documentation UI's token URL when `Service:PublicIdentityUrl` is unset.                                             |
| `globalSettings:baseServiceUri:internalIdentity`                     | None                                          | The token URL's source when neither setting above is set.                                                                                                              |
| `globalSettings:baseServiceUri:identity`                             | None                                          | The token URL's source when none of the settings above is set. With none, the OpenAPI document has no OAuth2 scheme.                                                   |
| `ForwardedHeaders:KnownProxies`                                      | None                                          | Comma-separated addresses or host names of proxies whose `X-Forwarded-For` and `X-Forwarded-Proto` are believed. Loopback is always trusted.                           |
| `ForwardedHeaders:KnownNetworks`                                     | None                                          | Comma-separated CIDR networks of such proxies.                                                                                                                         |
| `Tls:CertificateAuthorities`                                         | None                                          | PEM files of root certificate authorities trusted for outbound calls and the bearer handler's key fetches, besides the system's roots.                                 |
| `Tls:RequireHttps`                                                   | `false`                                       | When true, the bearer authority must be `https://`, or the service does not start.                                                                                     |
| `IpRateLimitOptions:Enabled`                                         | `true`                                        | Whether per-address limits apply. They are off when self-hosted, and when there are no rules, whatever this says.                                                      |
| `IpRateLimitOptions:GeneralRules`                                    | None                                          | The rules, each an `Endpoint` (default `*`), a `Period` (default `1m`), a `Limit` (default `0`, which refuses every request) and optional `Grants`.                    |
| `IpRateLimitOptions:EnableEndpointRateLimiting`                      | `true`                                        | Counts each method and path apart. When false, only `*` rules apply, counting all of an address's requests together.                                                   |
| `IpRateLimitOptions:IpWhitelist`                                     | None                                          | Addresses and CIDR networks never limited.                                                                                                                             |
| `IpRateLimitOptions:EndpointWhitelist`                               | None                                          | `verb:path` patterns never limited.                                                                                                                                    |
| `IpRateLimitOptions:HttpStatusCode`                                  | `429`                                         | The status a request over a limit is refused with.                                                                                                                     |
| `globalSettings:distributedIpRateLimiting:redisConnectionString`     | None                                          | A Redis where every instance of the service keeps one shared count. Unset, each process counts on its own.                                                             |
| `globalSettings:distributedIpRateLimiting:enabled`                   | `true`                                        | Whether a configured Redis is used.                                                                                                                                    |
| `globalSettings:distributedIpRateLimiting:maxRedisTimeoutsThreshold` | `10`                                          | How many Redis failures within the window set Redis aside, letting requests through unlimited until the window closes.                                                 |
| `globalSettings:distributedIpRateLimiting:slidingWindowSeconds`      | `120`                                         | The window, in seconds, those failures are counted in.                                                                                                                 |
| `OpenTelemetry:Enabled`                                              | `true` in the cloud, `false` when self-hosted | Turns the OTLP exporters on or off.                                                                                                                                    |
| `OpenTelemetry:Tracing:Enabled`                                      | `OpenTelemetry:Enabled`                       | Turns the trace exporter on or off.                                                                                                                                    |
| `OpenTelemetry:Metrics:Enabled`                                      | `OpenTelemetry:Enabled`                       | Turns the metric exporter on or off.                                                                                                                                   |
| `OpenTelemetry:Logging:Enabled`                                      | `OpenTelemetry:Enabled`                       | Turns the log exporter on or off.                                                                                                                                      |
| `DD_ENV` (environment variable)                                      | None                                          | Added to the telemetry resource as `env`.                                                                                                                              |
| `Service:Environment:CloudRegion`                                    | None                                          | A product service's `GET /api/v1/config` `environment.cloudRegion`. Omitted when unset.                                                                                |
| `Service:Environment:Vault`                                          | None                                          | The config resource's `environment.vault`, the web vault's address. Omitted when unset.                                                                                |
| `Service:Environment:FillAssistRules`                                | None                                          | The config resource's `environment.fillAssistRules`. Omitted when unset.                                                                                               |
| `Service:Settings:DisableUserRegistration`                           | `false`                                       | The config resource's `settings.disableUserRegistration`.                                                                                                              |
| `Service:Settings:SuppressOnboardingInterstitials`                   | `false`                                       | The config resource's `settings.suppressOnboardingInterstitials`.                                                                                                      |

The packages this one registers read their own settings: `Features:*` for feature flags
(Bitwarden.Server.Sdk.Features) and `Authentication:*` for bearer tokens
(Bitwarden.Server.Sdk.Authentication). The exporter reads the standard `OTEL_*` environment
variables, such as `OTEL_EXPORTER_OTLP_ENDPOINT`.

### Settings with a security trade-off

- **Trusted proxies (`ForwardedHeaders:*`).** A trusted proxy chooses the address a request is
  recorded and rate-limited under, so name only the proxies in front of the service, and keep
  `KnownNetworks` as narrow as the network they live on. Each proxy must add the address it took the
  request from last in `X-Forwarded-For`, and must strip the caller-context headers
  (`BitwardenHeaders.All`) a client sends. A value that does not parse fails closed: an address that
  doesn't parse is looked up as a host name and trusts nobody if the lookup fails, and a network
  that doesn't parse is ignored. Behind a proxy that is not trusted, every request carries the
  proxy's address, so all clients share one rate-limit allowance.
- **Internal TLS (`Tls:*`).** Every service that calls another must trust the authority that issued
  the callee's certificate, so name the same authority files on every service. A server
  certificate is trusted when the system trusts it or it chains to a named root; a host name that
  doesn't match is never trusted. A file that can't be read, or holds no certificate, stops the
  service at startup. With `Tls:RequireHttps`, a plain `http://` bearer authority stops the service
  at startup too.
- **Browser origins (`Service:BrowserOrigins`).** CORS decides which web pages may read a service's
  answers. Name each origin exactly as a browser sends it. `*`, an empty entry, a path, a trailing
  slash, a written-out default port, user info, a scheme other than `http` or `https`, or a `*`
  anywhere but the first label of the host stops the service at startup. Credentials are never
  admitted.
- **Shared rate-limit counts (`globalSettings:distributedIpRateLimiting:*`).** Every instance of a
  service must name the same Redis to share one allowance; counts are kept apart per service name.
  A key holds an unsalted SHA-256 of the rule, endpoint and address, which expires with its window.
  The IPv4 address space is small, so anyone who can read the Redis can recover the addresses:
  protect it as you would the logs. The connection string usually carries a password; keep it in
  secrets, not in `appsettings*.json`. When Redis fails the service lets requests through rather
  than refusing them.
- **Rate-limit rules (`IpRateLimitOptions:*`).** A `Period` that is not a number followed by `s`,
  `m`, `h` or `d`, or an `IpWhitelist` entry that is neither an address nor a CIDR network, stops the
  service when it starts. List the deployment's own service network in `IpWhitelist`, since services
  call each other and fetch their own tokens.
- **Documentation token URL (`Service:PublicIdentityUrl`).** A value that is not an absolute `http`
  or `https` URL stops the service at startup.

## Usage

The examples use a `Widget` resource and a `widget-service`. Types named `Widget...` other than the
resource are the service's own.

### Define a service

Derive from `Service`, pass an identifier and a title, and override the hooks you need. In a project
using `Microsoft.NET.Sdk.Web`:

```csharp
using Bitwarden.Server.Sdk.Service;

new WidgetService(args).Run();

internal sealed class WidgetService(string[] args) : Service("widget-service", "Widget Service", args)
{
    protected override AuthorizationDefaults Authorization => new()
    {
        Organization = ContextRequirement.Optional,
        ReadScope = "widgets.read",
        WriteScope = "widgets.write",
    };

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IWidgetStore, WidgetStore>();

    protected override void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        WidgetsEndpoints.Map(endpoints);
}
```

- **The identifier** is lowercase letters, digits and hyphens. It names the service's credential
  (`internal.{name}`), its token audience, its telemetry resource and its rate-limit keys. The
  constructor refuses anything else, which catches the title and identifier passed the wrong way
  round.
- **The title** is the documentation's and the OpenAPI document's title.
- **The hooks** run in this order: `CreateBuilder`, `ConfigureConfiguration`, the platform's
  registrations, `ConfigureServices`, every `IServiceExtension` a referenced package declares, then
  after the container is built `OnConfiguringPipeline`, the platform's middleware,
  `OnPipelineConfigured`, the platform's endpoints and `MapEndpoints`. Each documents what the base
  implementation does. `ConfigureServices` runs after the platform's registrations, so a service
  replaces one by registering its own.
- **`Build()`** returns the configured, unstarted `WebApplication`, for tests and hosting harnesses.
  `Run()` builds and starts it.

The platform's middleware runs in this order, outermost first: finishing writes a client stopped
waiting for, the forwarded client address, the log scope, the exception handler, status-code pages,
CORS, rate limiting, the caller-context refusal (product services), the documentation UI,
authentication, the caller-context check (domain services), authorization, and feature-flag checks.

A write (any method but `GET`, `HEAD`, `OPTIONS` and `TRACE`) runs to the end even when its client
disconnects: its `RequestAborted` token is never cancelled by the client leaving. A safe request
keeps the client's cancellation.

### Choose the kind of service

A **domain service** (the default) is called only by other services. It takes whom a request
concerns from the caller-context headers its caller sends. A **product service** is called by
people and is where a person's session ends. A **product domain service** is a domain service that
owns one product's data; the attribute only changes how its documentation looks.

```csharp
[ProductService]
internal sealed class WidgetPortal(string[] args) : Service("widget-portal", "Widget Portal", args);
```

The attributes are inherited, and a service can't carry both `[ProductService]` and
`[ProductDomainService]`. `IServiceIdentity.IsProductService` (Bitwarden.Server.Sdk.Common) reports
which kind is running.

|                                                        | Domain service (default)                                                                                                     | `[ProductService]`                                                                                     |
| ------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| Acting user (`IContext.UserId`)                        | `Bitwarden-User`, from another service                                                                                       | The validated token's `sub`                                                                            |
| Organization (`IContext.OrganizationId`)               | `Bitwarden-Organization`, from another service                                                                               | The token's signed `organization` claim                                                                |
| Provider (`IContext.ProviderId`)                       | `Bitwarden-Provider`, from another service                                                                                   | A provider session's `provider` claim                                                                  |
| Address, device and credential                         | `Bitwarden-Context`, from another service, parsed strictly (`400` when malformed)                                            | The connection's address, the session's `device` claim and the client's headers, and the kind of token |
| A request carrying a caller-context header             | Believed only from an internal service token (`client_id` `internal.*`) with a valid context token; ignored from anyone else | Refused `400` before authentication, whoever sends it                                                  |
| A token without a `sub` (or a `sub` that isn't a GUID) | Accepted                                                                                                                     | Refused `401`, except an organization credential or Send credential on an operation that accepts one   |
| Organization role and permissions (`ICallerRole`)      | Always `None`                                                                                                                | From the session's claims, counted only for the organization the session names                         |
| OpenAPI security schemes                               | The four caller-context headers, and the service credential when a token URL is known                                        | The session bearer token, and the service credential when a token URL is known                         |

The caller-context headers are `BitwardenHeaders.All`: `Bitwarden-Organization`, `Bitwarden-User`,
`Bitwarden-Provider`, `Bitwarden-Context` and `Bitwarden-Delegation`. A domain service believes them
only as the call's context token (`Bitwarden-Delegation`), signed by Identity for this service and
that caller, says. A context token that is missing beside a plain header, forged, expired, issued
for another callee or another caller is refused `401`; a plain header that disagrees with it is
refused `400`. A call with neither has an empty context.

A person's session speaks for one organization: a role counts only in the organization the session
names, so an owner of one organization can't act as an owner of another. A person's ordinary
session names no organization, so it reaches only operations that take none.

### Map endpoints under the standards

Each resource is a static class whose `Map` maps a group with `WithStandards()`, one line per
handler, and whose handlers carry one standard attribute each:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.JsonApi;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

[ApiTag(Description = "Widgets, which a person keeps.")]
public static class WidgetsEndpoints
{
    public const string Collection = "api/v1/widgets";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup(Collection).WithStandards();
        widgets.MapPost("", CreateWidget);
        widgets.MapGet("{id}", GetWidget);
        widgets.MapPut("{id}", UpdateWidget);
        widgets.MapDelete("{id}", DeleteWidget);
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
        ReadOneResponse(await store.FindAsync(id, cancellationToken)
            ?? throw new NotFoundException("widget", id));

    /// <summary>
    /// Replace a widget
    /// </summary>
    [StandardUpdate]
    public static async Task<ResourceDocument<Widget>> UpdateWidget(
        string id, ResourceDocument<Widget> request, IWidgetStore store, CancellationToken cancellationToken) =>
        UpdateResponse(await store.ReplaceAsync(id, request.Data, cancellationToken));

    /// <summary>
    /// Delete a widget
    /// </summary>
    [StandardDelete]
    public static Task DeleteWidget(string id, IWidgetStore store, CancellationToken cancellationToken) =>
        store.DeleteAsync(id, cancellationToken);
}
```

- **The attribute** gives the operation identifier, the documented responses, the success status
  (`201` and `Location` for a create, `204` for a handler that returns nothing), and the settled
  authorization. An `[Authorization]`, `[SelfHostedOnly]`, `[NotSelfHostedOnly]` or
  `[RequireFeature]` on the handler's class applies to every handler in it, and the class's name
  less an `Endpoints` suffix, split into words, is the OpenAPI tag unless `[ApiTag]` names one.
- **A request is bound and validated before the handler runs**, and refused in the standard error
  document. Resource documents, the query types below, plain JSON bodies, `[FromForm]` models and
  `[AsParameters]` models all bind and validate. A non-nullable reference member is required, and a
  resource's string member is limited to 255 characters unless it declares its own bound
  (`[StringLength]`, `[MaxLength]`, or `[Encrypted(MaxLength = ...)]`).
- **What a handler uses is its parameters:** its own services, `IContext` (Bitwarden.Server.Sdk.Common),
  `Person` for the acting user on an operation that requires one, and `HttpRequest` for a raw upload.
- **The service refuses to start** when a `Map` line's verb or route disagrees with the attribute,
  an operation identifier is used twice, a standard endpoint is missing from the OpenAPI document, or
  a declaration contradicts the operation's authorization, and it lists every problem at once.
- **Errors** are the standard error document. Throw the exceptions in Bitwarden.Server.Sdk.Common
  (`NotFoundException`, `ConflictException`, `InvalidRequestException` and the rest), and the
  exception handler answers with the matching status. A package can map a library's own exception to
  one of them by registering an `IExceptionTranslator` (Bitwarden.Server.Sdk.Common).
- **`[AcceptsInvalidRequests]`** runs a handler even when binding or validation failed, for a route
  whose shape somebody else fixed and which answers a bad request itself. A body it can't read binds
  as null, and its bytes are in the request's `UnreadableRequestBody` feature.
- **`IAuthorizesOperation`** on an attribute is a check made after authorization and before
  binding. Refuse by throwing `ForbiddenException`.

### Declare resources

A resource is a record deriving from `Resource`, with flat properties. The platform wraps them in
JSON:API's `attributes` on the wire, and the OpenAPI document describes the wire shape.

```csharp
using System.ComponentModel.DataAnnotations;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

[Sortable(nameof(CreatedAt), Default = true, Descending = true)]
[Filterable(nameof(CreatedAt))]
public sealed record Widget : Resource
{
    /// <summary>
    /// What the widget is called.
    /// </summary>
    [Sortable]
    [Filterable]
    [StringLength(100)]
    public required string Name { get; init; }

    /// <summary>
    /// The person's notes, encrypted on their device.
    /// </summary>
    [Encrypted(MaxLength = 10_000)]
    public string? Notes { get; init; }
}
```

- **The type** is the record's name in kebab case (`Widget` is `widget`). `[ResourceType("...")]`
  overrides it.
- **Inherited members**: `Id`, `Type`, `Version`, and the read-only `CreatedAt`, `CreatedBy`,
  `UpdatedAt`, `UpdatedBy`, `DeletedAt` and `DeletedBy`.
- **`[ReadOnly]`** (`Bitwarden.Server.Sdk.Service.JsonApi`) marks a member a caller reads but never
  sets. **`[Verbatim]`** keeps a string as sent: not trimmed, and a blank value kept.
- **Enumerations** travel as their names in upper snake case (`EnumValues`), and an unknown name is
  refused.
- **Dates** are written as ISO 8601 in UTC with a literal `Z` (`2026-01-02T03:04:05.5Z`), the
  fraction to the tick with trailing zeros left off. Reading accepts any ISO 8601 offset. `UtcDates`
  gives the same text for hand-written strings.
- **A member typed `JsonElement` or a `JsonNode`** takes any JSON value. The service checks it
  itself.

### Return standard responses

`StandardResponses` builds each operation's document: `CreateResponse`, `ReadOneResponse`,
`ReadManyResponse`, `UpdateResponse` and `ActionResponse`. A create's `Location` and a page's links
are derived from the request when the result is written.

An action is a named operation on a resource, `POST {resource}/actions/{action}`. Its name is the
method's, in kebab case, unless one is passed. Its body is a plain JSON object of arguments, not a
resource document. It answers `200` with what the handler returns, or `204` when it returns nothing.

```csharp
widgets.MapPost("{id}/actions/polish", Polish);
widgets.MapPost("current/actions/reset", Reset);

[StandardAction]
public static async Task<ResourceDocument<Widget>> Polish(
    string id, PolishWidget request, IWidgetStore store, CancellationToken cancellationToken) =>
    ActionResponse(await store.PolishAsync(id, request.Shine, cancellationToken));

[StandardAction("reset", template: "current")]
public static Task Reset(IWidgetStore store, CancellationToken cancellationToken) =>
    store.ResetAsync(cancellationToken);

public sealed record PolishWidget(int Shine);
```

`template` replaces only the resource segment (`{id}` by default); the `actions/{action}` after it
is always the platform's.

### Query a collection

Every query parameter is an ordinary handler parameter, so the signature is the whole request.

| Feature       | Declare                                                                         | Apply with                                                                                   |
| ------------- | ------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| Filter        | `Filter<Guid>? owner`, `Filter<List<Guid>>? id` (`filter[owner]`, `filter[id]`) | Your own `Where`                                                                             |
| Range filter  | `Filter<Range<DateTimeOffset>>? createdAt`                                      | `query.Within(e => e.CreatedAt, createdAt?.Value)`                                           |
| Sort          | `Sort? sort`                                                                    | `query.SortedBy(sort ?? Sort.Default<Widget>(), e => e.Id)`                                  |
| Offset paging | `PageNumber pageNumber = default`, `PageSize pageSize = default`                | `query.Page(page)`, then `rows.ToPagedAsync(page, map, query.CountAsync, cancellationToken)` |
| Cursor paging | `PageSize pageSize = default`, `PageAfter pageAfter = default`                  | `query.Page(page, sort, e => e.Id)`, then `rows.ToCursorPage(page, sort, e => e.Id, map)`    |
| Sparse fields | `Fields<Widget> fields = default` (`fields[widget]`)                            | `ReadOneResponse(resource, fields)`, `ReadManyResponse(page, requested, fields)`             |

An offset-paged list, with Entity Framework Core and the service's own `WidgetDatabase` and
`WidgetEntity`:

```csharp
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.EntityFrameworkCore;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

/// <summary>
/// List widgets
/// </summary>
/// <param name="id">Only these widgets.</param>
/// <param name="createdAt">Only widgets created within this range.</param>
[StandardReadMany]
public static async Task<PagedResourceListDocument<Widget>> ListWidgets(
    Filter<List<Guid>>? id,
    Filter<Range<DateTimeOffset>>? createdAt,
    Sort? sort,
    WidgetDatabase database,
    Fields<Widget> fields = default,
    PageNumber pageNumber = default,
    PageSize pageSize = default,
    CancellationToken cancellationToken = default)
{
    var page = new Page { Number = pageNumber, Size = pageSize };

    var widgets = database.Widgets.AsNoTracking();
    if (id is not null)
    {
        widgets = widgets.Where(widget => id.Value.Contains(widget.Id));
    }

    widgets = widgets.Within(widget => widget.CreatedAt, createdAt?.Value);

    var rows = await widgets
        .SortedBy(sort ?? Sort.Default<Widget>(), widget => widget.Id)
        .Page(page)
        .ToListAsync(cancellationToken);

    return ReadManyResponse(
        await rows.ToPagedAsync(page, ToResource, widgets.CountAsync, cancellationToken),
        page,
        fields);
}

private static Widget ToResource(WidgetEntity entity) =>
    new() { Id = entity.Id.ToString(), Name = entity.Name, CreatedAt = entity.CreatedAt };
```

- **Filters.** The parameter's name is the filter's (`id` reads `filter[id]`), its type is what the
  filter accepts, and nullability makes it optional. A collection type accepts repeated parameters
  and comma-separated lists. A filter no parameter declares is refused rather than ignored.
- **Ranges.** `[` and `]` include a bound, `(` and `)` exclude it, and `*` leaves it open:
  `filter[createdAt]=[2026-01-01T00:00:00Z,*)`. A range that is inverted, empty or doesn't parse is
  refused `422`.
- **Sorting.** A caller writes `sort=-createdAt,name`, a leading `-` for descending. Only fields the
  resource marks `[Sortable]` are accepted, and exactly one sortable field sets `Default`.
  `MappedTo` names the entity property a field is stored as when the names differ. Always add a
  unique tie-break (the identifier), so rows sharing a sort value are never repeated or skipped
  across pages.
- **Offset paging.** `page[number]` defaults to 1 and `page[size]` to 50, or to a `[DefaultValue]` on
  the `PageSize` parameter. A size above 200 is reduced to 200; a number or size below 1 is refused.
  `meta` carries `pageCount`, `pageNumber`, `pageSize` and `totalCount`, and the links carry the
  caller's filters and sort.
- **Counting only when needed.** `ToPagedAsync` runs the count you pass only when the page can't
  settle the total. A page with fewer rows than it holds is the last one, so most reads cost one
  query. A full page counts, and so does an empty page past the first. Count the query the page was
  taken from, without the page. `atMost` passes a bound a unique key guarantees. A list already in
  memory uses `all.ToPaged(page)`, which counts nothing.
- **Cursor paging** never counts and is stable while rows are written, for a large collection read in
  order. A cursor is opaque, records the sort it was issued under and is refused under another. A
  nullable sort field can't be paged by cursor. `meta` carries `pageSize`, `hasMore` and, when more
  follow, `cursor`.
- **Sparse fieldsets.** `fields[widget]=name,notes` returns only those attributes; `id` and `type`
  always come back. Absent, every attribute is returned. An unknown attribute is refused `422`. Use
  `fields.Includes("name")` to skip work for an attribute a caller left out.

A cursor-paged list:

```csharp
[StandardReadMany]
public static async Task<CursorPagedResourceListDocument<WidgetEvent>> ListWidgetEvents(
    Sort? sort,
    WidgetDatabase database,
    PageSize pageSize = default,
    PageAfter pageAfter = default,
    CancellationToken cancellationToken = default)
{
    var page = new CursorPage { Size = pageSize, After = pageAfter };
    var order = sort ?? Sort.Default<WidgetEvent>();

    var rows = await database.WidgetEvents.AsNoTracking()
        .Page(page, order, row => row.Id)
        .ToListAsync(cancellationToken);

    return ReadManyResponse(rows.ToCursorPage(page, order, row => row.Id, ToResource), page);
}
```

### Run bulk operations and searches

Bulk operations and searches are `POST`s to a suffix on the collection
(`api/v1/widgets:bulk-delete`), so their attribute takes the collection's path and their `Map` line
is on the route builder itself, not the group:

```csharp
endpoints.MapPost($"{Collection}:bulk-create", CreateWidgets).WithStandards();
endpoints.MapPost($"{Collection}:bulk-delete", DeleteWidgets).WithStandards();
endpoints.MapPost($"{Collection}:search", SearchWidgets).WithStandards();

[StandardBulkCreate(Collection)]
public static async Task<BulkCreatedDocument<Widget>> CreateWidgets(
    BulkCreate<Widget> request, IWidgetStore store, CancellationToken cancellationToken)
{
    BulkRequests.RequireAtMost(request.Data.Count, 100, "/data");
    return BulkCreatedDocument<Widget>.For(request, await store.AddAllAsync(request.Data, cancellationToken));
}

[StandardBulkDelete(Collection, Targeting = Targeting.Ids)]
public static async Task<BulkResult> DeleteWidgets(
    BulkDelete<Widget> request, IWidgetStore store, CancellationToken cancellationToken)
{
    var ids = request.Ids ?? [];
    BulkRequests.RequireAtMost(ids.Count, 500, "/ids");
    BulkRequests.RequireDistinct(ids);
    return BulkResult.Affected(await store.DeleteAllAsync(ids, cancellationToken));
}

[StandardSearch(Collection)]
public static async Task<PagedResourceListDocument<Widget>> SearchWidgets(
    Search<Widget> request,
    WidgetDatabase database,
    PageNumber pageNumber = default,
    PageSize pageSize = default,
    CancellationToken cancellationToken = default)
{
    var page = new Page { Number = pageNumber, Size = pageSize };
    var widgets = database.Widgets.AsNoTracking().Matching(request.Filter);
    var rows = await widgets.SortedBy(Sort.Default<Widget>(), widget => widget.Id).Page(page).ToListAsync(cancellationToken);

    return ReadManyResponse(await rows.ToPagedAsync(page, ToResource, widgets.CountAsync, cancellationToken), page);
}
```

- **The operations** are `[StandardBulkCreate]` (`BulkCreate<T>`), `[StandardBulkUpdate]`
  (`BulkUpdate<T>`), `[StandardBulkReplace]` (`BulkReplace<T>`), `[StandardBulkDelete]`
  (`BulkDelete<T>`), `[StandardBulkAction]` (`BulkAction<T>` or `BulkAction<T, TInput>`, at
  `{collection}:bulk-{action}`) and `[StandardSearch]` (`Search<T>`). A bulk action can't be named
  `create`, `update`, `replace` or `delete`.
- **Targeting.** A bulk update, delete or action names its resources by `ids` or by a `filter`
  expression; `Targeting` says which the operation accepts (`Either` by default).
- **Expressions** are JSON Logic, limited to `var`, `==`, `!=`, `>`, `>=`, `<`, `<=`, `in`, `and`,
  `or` and `!`. Only fields the resource marks `[Filterable]` may be named; anything else is refused
  `422`. `Matching` turns an expression into a query predicate.
- **Atomicity.** A caller sends `atomic=true` (the default) or `atomic=false`. `Supports` on the
  attribute says which the operation offers, and the platform refuses any other `422`. An
  all-or-nothing operation answers `BulkResult.Affected(count)` or throws `BulkRefusedException` with
  every error; a best-effort one answers `BulkResult.BestEffort(affected, failures)`, describing each
  failure with `BulkFailure.At(exception, pointer)`.
- **Local identifiers.** Each item of a bulk create may carry a `lid` beside its `type`: a non-empty
  string unique within the request. The platform refuses a malformed or repeated one `422` at
  `/data/{n}/lid`, and keeps them in `BulkCreate<T>.Lids`. `BulkCreatedDocument<T>.For` echoes each `lid` beside the created resource's `id`, adds `meta.affectedCount`, and throws
  if there isn't exactly one created resource for each item.
- **Request checks.** `BulkRequests.RequireAtMost` and `RequireDistinct` refuse a request `422`
  before any resource is touched; each operation documents its own maximum.
- **Versions.** `Concurrency.Require(sent, stored)` refuses `409` when the caller sent a `version`
  that no longer matches, and passes a caller who sent none.

### Authorize operations

Every operation is authenticated unless it says otherwise. Requirements come from three levels, the
most specific winning: the service's `Authorization` defaults, an `[Authorization]` on the handler
class, and one on the handler.

```csharp
[Authorization(Scope = "widgets.admin", Organization = ContextRequirement.Required)]
[StandardDelete]
public static Task DeleteSharedWidget(string id, IWidgetStore store, CancellationToken cancellationToken) =>
    store.DeleteAsync(id, cancellationToken);

[Authorization(User = ContextRequirement.Required)]
[StandardReadOne("current")]
public static async Task<ResourceDocument<Widget>> GetMyWidget(
    Person person, IWidgetStore store, CancellationToken cancellationToken) =>
    ReadOneResponse(await store.FindForAsync(person.Id, cancellationToken)
        ?? throw new NotFoundException("widget", "current"));
```

- **Scopes.** Set `ReadScope` and `WriteScope` on the service's `AuthorizationDefaults`, so every
  operation demands one by what kind of operation it is. Without them, an operation that declares no
  scope demands none.
- **Context.** `Organization`, `User` and `Provider` are each `Required`, `Optional` or `Forbidden`.
  The defaults are organization required, user optional and provider forbidden; an operation that
  answers for a provider says so.
- **Roles and permissions** (`OrganizationRole`, `Permission`, `ProviderRole`) apply in product
  services only. A role is satisfied by it or any role above it, and an owner or administrator holds
  every permission. `ICallerRole` reads them.
- **`Anonymous = true`** opens an operation to callers with no token, for the operational surface.
  Nothing that touches a caller's data should set it.
- **`Person`** is the acting user as a handler parameter. A handler taking one must require a user,
  or the service refuses to start.
- **`[AnyScope("a.read", "a.invitations")]`** admits a caller holding any one of several scopes. The
  operation must declare its organization and user optional, its provider optional or forbidden, and
  no role or permission, and then decides itself what each scope may ask, with
  `CallerScopes.Holds(principal, scope)`.
- **`[SelfHostedOnly]` and `[NotSelfHostedOnly]`** refuse an operation `422` in the other kind of
  install.

### Accept other credentials and context

These declarations go on one handler each, so every exception is a reviewed decision where it is
read. The service refuses to start when one contradicts the operation's authorization.

- **`[AcceptsOrganizationCredential("api.organization")]`** (product services) also accepts a token
  issued to an organization rather than a person: an `organization` claim and no `sub`. The
  organization is the token's, there is no user, and the caller is the organization's `Owner`. The
  scope named on the attribute replaces the operation's own. Every other operation keeps refusing
  such a token `401`.
- **`[AcceptsSendCredential]`** (product services) accepts the token minted for one Send's link. The
  operation must name a scope and forbid both a user and an organization. `SendCredential.SendId`
  reads which Send the token names.
- **`[NamesOrganization]` and `[NamesProvider]`** (product services) mark an operation about an
  organization or provider the person can't hold a session for yet, such as accepting an invitation.
  The operation names it in its path (`{organizationId}`, `{providerId}`) or in a body implementing
  `INamesOrganization` or `INamesProvider`. It must require a person, forbid the session from naming
  one, and demand no role. The organization named never enters this service's `IContext`; it is
  carried only on the operation's calls to other services.
- **`[CreatesOrganization]`** (product services) lets an operation that creates an organization name
  it on its later calls in the same request. It must forbid an organization and require a person.
- **`[AcceptsAssertedContext(reason, callers)]`** (domain services). A domain service refuses `403`
  an asserted context (the calling service's own word, rather than one delegated from the person's
  own token) that names a person, unless the operation accepts it from the services it names:

```csharp
[AcceptsAssertedContext("The widget jobs expire widgets on their owners' behalf.", "widget-jobs")]
[StandardAction]
public static Task Expire(string id, IWidgetStore store, CancellationToken cancellationToken) =>
    store.ExpireAsync(id, cancellationToken);
```

An asserted context naming nobody is accepted everywhere, and a delegated one always is.

### Accept only tokens addressed to the service

Every service accepts only tokens whose `aud` names it, and this is not a setting. The expected
audience is the identifier passed to `Service` (`IServiceIdentity.Audience`). At a product service
that is a person's token exchanged for that product; at a domain service, a service's token issued
for this callee. A token leaked at one service is refused at every other. A token naming several
audiences is accepted when one is this service's.

A service that must accept another audience at one surface it hosts adds it to
`JwtBearerOptions.TokenValidationParameters.ValidAudiences` in `ConfigureServices`. The platform
keeps its own audience beside it, and the service is responsible for keeping the extra audience to
that surface.

### Verify another issuer's tokens

`DiscoveredIssuerTokens` verifies an access token another OpenID Connect issuer signed, as a bearer
handler pointed at that issuer would: a key its discovery document publishes, the issuer the document
names, the lifetime with five minutes' skew, and the type `at+jwt`, with no audience check.

```csharp
var issuer = new DiscoveredIssuerTokens(new Uri("https://identity.example.com"), clients.CreateClient("issuer"));
var result = await issuer.ValidateAsync(token, cancellationToken);
if (result.IsValid)
{
    var subject = result.ClaimsIdentity.FindFirst("sub")?.Value;
}
```

The document and keys are read on first use and kept; a token naming an unknown key has them read
again. Keep one instance per issuer. The token is never logged.

### Publish the API's documentation

Every service serves its OpenAPI 3.0 document at `/api/openapi.json` and a documentation UI at
`/docs`, both anonymous, with assets embedded in the package. `GET /` and `HEAD /` redirect (`302`,
relative) to `/docs/index.html`. The document is generated from the handlers' signatures, types,
data annotations and XML documentation, so enable `GenerateDocumentationFile` in the service.

- **Sections.** Give every handler class an `[ApiTag(Description = "...")]`. The name defaults to
  the class name less `Endpoints`, split into words.
- **Committing the contract.** Run the service with `--emit-openapi <path>` to write the document
  and exit without serving. The service's own hosted services don't run, so no database or other
  dependency needs to be reachable. Writing fails when a section has no description.
- **Security schemes.** The OAuth2 client-credentials scheme's token URL is
  `Service:PublicIdentityUrl` or the bearer authority, followed by `/connect/token`. A cross-origin
  token request from the UI also needs Identity to admit the documentation's origin.

### Admit browser origins

A service a browser calls cross-origin lists the origins it admits:

```json
{
    "Service": {
        "BrowserOrigins": ["https://vault.example.com", "https://*.example.com"]
    }
}
```

An entry starting `*.` also admits that host's subdomains on the same scheme and port. Methods are
`GET`, `POST`, `PUT` and `DELETE`. Admitted request headers are `Accept`, `Authorization`,
`Bitwarden-Client-Name`, `Bitwarden-Client-Version`, `Bitwarden-Package-Type`, `Cache-Control`,
`Content-Type`, `Device-Identifier`, `Device-Type`, `Is-Prerelease`, `Pragma`, `User-Agent`,
`X-Device-Identifier` and `X-Requested-With`. The caller-context headers are never admitted, so a
page's preflight fails before it could send one, and credentials (`Access-Control-Allow-Credentials`)
never are. CORS runs inside the exception handler and ahead of authentication, so a preflight is
answered `204` without a token, and a refused request still carries the headers a page needs to read
why.

### Trust a proxy's client address

A request's address is its connection's. When the connection is a trusted proxy's and the request
carries `X-Forwarded-For`, it is the last address there: the one the proxy added. The last
`X-Forwarded-Proto` (`http` or `https`) from a trusted proxy is the request's scheme, so a cookie
whose policy is `SameAsRequest` is `Secure` behind a proxy that terminates TLS.

```json
{
    "ForwardedHeaders": {
        "KnownProxies": "proxy",
        "KnownNetworks": "10.0.0.0/8"
    }
}
```

A host name in `KnownProxies` is looked up when a request comes from an untrusted address, and again
at most every 30 seconds, so a proxy that starts later or moves is trusted without a restart. A
product service records this address and passes it on in `Bitwarden-Context`; since it refuses that
header from a client, a client can't choose the address its actions are recorded with.

### Limit requests per address

Rules in the `IpRateLimitOptions` section count requests per client address in fixed windows:

```json
{
    "IpRateLimitOptions": {
        "IpWhitelist": ["10.0.0.0/8"],
        "GeneralRules": [
            { "Endpoint": "post:*", "Period": "1s", "Limit": 5 },
            {
                "Endpoint": "post:/connect/token",
                "Period": "1m",
                "Limit": 10,
                "Grants": ["password"]
            }
        ]
    }
}
```

- **Matching.** `Endpoint` is `*` or `verb:path`, with `*` for any run of characters and `?` for any
  one, matched without case against the request's path in lower case, without a trailing slash and
  with repeated slashes collapsed. `Period` is a number and `s`, `m`, `h` or `d`.
- **Counting.** With `EnableEndpointRateLimiting`, a rule applies when it matches the request's
  `verb:path` or `*:path`, and counts each method and path apart, so `post:*` gives every `POST`
  route its own allowance. Of the rules that apply, the most restrictive for each period counts,
  shortest period first, and counting stops at the first over its limit.
- **Grants.** A rule with `Grants` counts a token request only when its form names one of those
  `grant_type`s.
- **Refusal.** A request over a limit is refused `429` with `Retry-After` and the standard error
  document, code `too-many-requests`, detail `Slow down! Too many requests. Try again in 1m.` A
  service that must answer in another body registers its own `IIpRateLimitRefusal`.
- **Off** when self-hosted, when `Enabled` is false, and when there are no rules.
- **Shared counts.** With `globalSettings:distributedIpRateLimiting:redisConnectionString`, every
  instance counts in one Redis, under keys `{service}:ip-rate-limit:{SHA-256}` that expire with
  their window. A service counting its own limits uses `IRateLimitCounters`, and `IpRateLimitRules`
  applies the same rule choice to rules of its own.

### Guard outbound HTTP against SSRF

A client that sends requests to an address someone outside Bitwarden chose (an organization's identity
provider, a webhook) adds the SSRF guard. Each request's host is resolved first, and an address in a
private, loopback, link-local or other reserved range is refused with `SsrfProtectionException`
before anything connects. The guard follows redirects itself and checks every hop.

```csharp
services.AddHttpClient("widget-webhooks").AddSsrfProtection();
services.AddHttpClient("widget-identity-provider").AddCloudSsrfProtection();
```

`AddSsrfProtection(followRedirects: false)` returns redirects to a caller that follows its own.
`AddCloudSsrfProtection()` guards only in Bitwarden's cloud and leaves a self-hosted install's
client unguarded, since a self-hosted identity provider often runs on the same private network. The
extensions are in the `Microsoft.Extensions.DependencyInjection` namespace. `IPAddress.IsInternal()`
(`IPAddressExtensions`) is the check the guard makes.

### Call other services over TLS

A service serves HTTPS with Kestrel's own settings (`Kestrel:Endpoints:Https:*`). For its outbound
calls it applies `InternalTls` from Bitwarden.Server.Sdk.Common: `Tls:CertificateAuthorities` adds
root authorities to the trust of every client the HTTP client factory makes and of the bearer
handler's discovery and key fetches, and `Tls:RequireHttps` refuses a plain-HTTP bearer authority at
startup. A client that sets its own primary handler after the platform keeps its own trust. TLS
gives these calls a Secure Channel; with the tokens each call carries, a Trusted Channel.

### Export telemetry

Every service exports traces, metrics and logs over OTLP. The resource is named by the service's
identifier. Spans come from ASP.NET Core, `HttpClient`, SqlClient and every `Bitwarden.*` and
`Azure.*` activity source; metrics from ASP.NET Core, `HttpClient`, SqlClient, the runtime and every
`Bitwarden.*` and `Bit.*` meter. Logs carry their scopes, so the organization, user and provider a
request is about travel with each record.

- **The switches** turn exporters on or off only: spans are created either way, so a trace continues
  through a service that exports nothing. A caller's W3C `traceparent` continues in the service, and
  every outgoing `HttpClient` call carries it.
- **Not traced:** the health probes.
- **Query values are redacted**, as the instrumentation does by default, because a query can carry a
  credential.
- **Nothing is exported** while the process only writes its OpenAPI document (`--emit-openapi`).
- **Not instrumented:** Entity Framework Core and StackExchange.Redis, whose instrumentation
  packages are prereleases. SqlClient's spans cover SQL Server.

### Host a self-hosted install

`globalSettings:selfHosted: true` marks a self-hosted install. Then:

- `appsettings.SelfHosted.json`, when present, is layered in after `appsettings.{Environment}.json`,
  so it overrides the service's own settings and is overridden by user secrets, environment
  variables and the command line;
- `IBitwardenEnvironment.SelfHosted` is true, and `globalSettings:selfHostFlavor` is the flavor;
- per-address rate limits are off, telemetry exporters default to off, and
  `AddCloudSsrfProtection()` clients are unguarded;
- `[SelfHostedOnly]` operations run and `[NotSelfHostedOnly]` ones are refused `422`;
- the config resource reports `settings.selfHosted: true`.

### Publish configuration and feature flags

Every service gets `IFeatureService` from Bitwarden.Server.Sdk.Features, configured from
`Features:*`: LaunchDarkly when `Features:LaunchDarkly:SdkKey` is set, otherwise
`Features:FlagValues`, served offline. The SDK key is a secret: keep it in user secrets or the
environment, never in `appsettings*.json`.

`[RequireFeature("flag-key")]` on a handler or its class holds it behind a flag. The check runs after
authentication and authorization, so a caller the operation would refuse is refused `401` or `403`
whatever the flag. With the flag off, an authorized caller gets `404` in the standard error
document, the same answer as an address that matches nothing.

A product service publishes `GET /api/v1/config`, which a client reads before and after sign-in:

```json
{
    "data": {
        "type": "config",
        "attributes": {
            "version": "1.0.0",
            "environment": {
                "cloudRegion": "US",
                "vault": "https://vault.example.com",
                "fillAssistRules": "https://example.com/fill-assist-rules.json"
            },
            "settings": {
                "disableUserRegistration": false,
                "suppressOnboardingInterstitials": false,
                "selfHosted": false
            },
            "featureFlags": {
                "pm-12345-widget-flag": true
            }
        }
    }
}
```

- **`version`** is the service's version, from `IBitwardenEnvironment`, or its assembly's
  informational version without build metadata.
- **`environment`** and **`settings`** come from `Service:Environment:*` and `Service:Settings:*`;
  `selfHosted` from `globalSettings:selfHosted`.
- **`featureFlags`** is `IFeatureService.GetAll()`: only flags listed in `Features:KnownFlags`.
- **Who may read it.** Anyone. A token is still honoured when sent, and the flags are then evaluated
  for the person; a token that fails validation gets the anonymous answer.

The flags are evaluated for a LaunchDarkly context the platform builds. Outside a request it is a
single anonymous context under a shared anonymous key. Inside one it is a multi-context:

- a `user` context keyed by the acting user, or by the shared anonymous key, marked anonymous, when
  there is none, with `organizations`: the person's confirmed memberships, from the token's
  `organizations` claim (or, in a domain service, from the caller's context);
- a `device` context keyed by the token's `device` claim, else the `Device-Identifier` header, when
  the value matches `^[A-Za-z0-9_:-]{1,50}$`;
- on both, `client-version` and `client-version-is-prerelease` from `Bitwarden-Client-Version` and
  `Is-Prerelease: 1`, and `device-type` and `client-type` from `Device-Type`, or the `devicetype`
  claim when there is no header.

With an SDK key, this context goes to LaunchDarkly: user and device identifiers, client version and
type, and organization identifiers. No Vault Data. Register a different builder with
`services.AddContextBuilder<T>()` in `ConfigureServices`.

### Cache a downstream answer briefly

A service that holds another service's answer in process memory for a moment uses
`GetOrResolveOnceAsync` (`Bitwarden.Server.Sdk.Service.Caching`) instead of
`IMemoryCache.GetOrCreateAsync`, so concurrent requests that miss together share one resolution:

```csharp
var enabled = await cache.GetOrResolveOnceAsync(
    ("widget-enabled", organizationId),
    TimeSpan.FromSeconds(30),
    () => widgets.IsEnabledAsync(organizationId),
    cancellationToken);
```

A failed or cancelled resolution is not kept, and no caller's cancellation token reaches the shared
resolution, so one caller giving up fails nobody else. Use it only for answers from other services:
an answer read from the request's own database belongs to that request's scope.

### Answer health probes

`GET /api/v1/health`, `/api/v1/readiness` and `/api/v1/startup` are anonymous, tagged `System`, and
answer a `SystemStatus`. Readiness asks every registered health check and answers `503` with what is
missing when any is unhealthy, without exception text.

### Extend the platform from a package

- **`IServiceExtension`** (Bitwarden.Server.Sdk.Common): a package declares one with
  `[assembly: ServiceExtension(typeof(...))]`, and every service referencing it applies it after
  `ConfigureServices`, in a fixed order.
- **`IStandardEndpointFilterProvider`**: adds an endpoint filter to every standard endpoint it applies
  to, between the platform's checks and the handler.
- **`IStandardEndpointCheck`**: a rule checked over every endpoint once all are built, reported with
  the startup check's other problems.
- **`ServedByPlatform<T>()`** is the body of a `[StandardReadOne]` handler whose read by identifier a
  registered filter answers. The handler still declares the route, operation and documentation; its
  body throws if nothing answered.

## Types

### `Bitwarden.Server.Sdk.Service`

| Type                                     | Purpose                                                                                         |
| ---------------------------------------- | ----------------------------------------------------------------------------------------------- |
| `Service`                                | The base class every service derives from; owns startup.                                        |
| `ProductServiceAttribute`                | Marks a service people call with their own session.                                             |
| `ProductDomainServiceAttribute`          | Marks a domain service that owns one product's data; changes its documentation's look only.     |
| `StandardEndpointExtensions`             | `WithStandards()`, which holds a group's endpoints to the standards.                            |
| `StandardResponses`                      | Builds each standard operation's response document.                                             |
| `StandardCreateAttribute`                | A create: `POST` to the collection, `201`.                                                      |
| `StandardReadOneAttribute`               | A read of one resource: `GET {id}`, `200`.                                                      |
| `StandardReadManyAttribute`              | A read of a collection: `GET`, `200`.                                                           |
| `StandardUpdateAttribute`                | A replace: `PUT {id}`, `200`.                                                                   |
| `StandardDeleteAttribute`                | A delete: `DELETE {id}`, `204`.                                                                 |
| `StandardActionAttribute`                | A named action: `POST {id}/actions/{action}`.                                                   |
| `StandardBulkCreateAttribute`            | `POST {collection}:bulk-create`.                                                                |
| `StandardBulkUpdateAttribute`            | `POST {collection}:bulk-update`.                                                                |
| `StandardBulkReplaceAttribute`           | `POST {collection}:bulk-replace`.                                                               |
| `StandardBulkDeleteAttribute`            | `POST {collection}:bulk-delete`.                                                                |
| `StandardBulkActionAttribute`            | `POST {collection}:bulk-{action}`.                                                              |
| `StandardSearchAttribute`                | `POST {collection}:search`, a read by expression.                                               |
| `IStandardOperation`                     | What every standard operation attribute declares.                                               |
| `ApiTagAttribute`                        | Names and describes a handler class's OpenAPI section.                                          |
| `AuthorizationAttribute`                 | What a caller must satisfy to invoke an operation.                                              |
| `AuthorizationDefaults`                  | What a service's operations demand unless they say otherwise.                                   |
| `ContextRequirement`                     | Whether an organization, user or provider is required, optional or forbidden.                   |
| `OrganizationRole`                       | A caller's role in the session's organization, ordered by reach.                                |
| `OrganizationPermission`                 | A permission a custom member may be granted.                                                    |
| `ProviderRole`                           | A caller's role in the session's provider.                                                      |
| `ICallerRole`                            | Reads the caller's roles and permissions in a product service.                                  |
| `CallerScopes`                           | Whether the caller's token carries a scope.                                                     |
| `AnyScopeAttribute`                      | Admits a caller holding any one of several scopes.                                              |
| `AcceptsOrganizationCredentialAttribute` | Lets a product service operation accept an organization's token.                                |
| `AcceptsSendCredentialAttribute`         | Lets a product service operation accept a Send's token.                                         |
| `SendCredential`                         | Reads the Send a token names.                                                                   |
| `AcceptsAssertedContextAttribute`        | Lets a domain service operation accept an asserted context naming a person from named services. |
| `InternalCaller`                         | Whether a caller presented an internal service token.                                           |
| `NamesOrganizationAttribute`             | Marks an operation that names an organization the session can't.                                |
| `NamesProviderAttribute`                 | Marks an operation that names a provider the session can't.                                     |
| `INamesOrganization`                     | A request body naming the organization its action is about.                                     |
| `INamesProvider`                         | A request body naming the provider its action is about.                                         |
| `CreatesOrganizationAttribute`           | Lets an operation that creates an organization name it on its later calls.                      |
| `DiscoveredIssuerTokens`                 | Verifies an access token another OpenID Connect issuer signed.                                  |
| `Person`                                 | The acting user, as a handler parameter.                                                        |
| `IAuthorizesOperation`                   | An attribute's check made after authorization and before binding.                               |
| `AcceptsInvalidRequestsAttribute`        | Runs a handler even when binding or validation failed.                                          |
| `UnreadableRequestBody`                  | The bytes of a body an `[AcceptsInvalidRequests]` handler couldn't bind.                        |
| `SelfHostedOnlyAttribute`                | An operation only a self-hosted install has.                                                    |
| `NotSelfHostedOnlyAttribute`             | An operation only a cloud install has.                                                          |
| `EnumValues`                             | How enumeration members appear on the wire.                                                     |
| `UtcDates`                               | Writes dates as ISO 8601 in UTC with a literal `Z`.                                             |
| `SystemStatus`                           | The health probes' answer.                                                                      |

### `Bitwarden.Server.Sdk.Service.Endpoints`

| Type                              | Purpose                                                 |
| --------------------------------- | ------------------------------------------------------- |
| `IStandardEndpointFilterProvider` | Adds a filter to every standard endpoint it applies to. |
| `IStandardEndpointCheck`          | A rule checked over every endpoint at startup.          |

### `Bitwarden.Server.Sdk.Service.JsonApi`

| Type                                   | Purpose                                                   |
| -------------------------------------- | --------------------------------------------------------- |
| `Resource`                             | The base record of every resource.                        |
| `ResourceTypeAttribute`                | Overrides a resource's type name.                         |
| `ReadOnlyAttribute`                    | Marks a member a caller reads but never sets.             |
| `VerbatimAttribute`                    | Keeps a string member as the caller sent it.              |
| `ResourceDocument<T>`                  | A document with one resource, for requests and responses. |
| `ResourceListDocument<T>`              | A document with a list of resources.                      |
| `PagedResourceListDocument<T>`         | An offset-paged list, with paging `meta` and links.       |
| `CursorPagedResourceListDocument<T>`   | A cursor-paged list.                                      |
| `PageLinks`                            | A paged list's `first`, `prev`, `next` and `last` links.  |
| `BulkCreatedDocument<T>`               | A bulk create's answer, echoing each `lid`.               |
| `ResourceDocumentJsonConverterFactory` | Applies the JSON:API `attributes` envelope.               |

### `Bitwarden.Server.Sdk.Service.Querying`

| Type                                                                                                                          | Purpose                                                      |
| ----------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------ |
| `Filter<T>`                                                                                                                   | A `filter[...]` parameter.                                   |
| `Range<T>`                                                                                                                    | A range a filter accepts.                                    |
| `RangeQueryable`                                                                                                              | `Within`, which narrows a query to a range.                  |
| `Sort`, `SortBy`, `SortDirection`                                                                                             | The order a caller asked for.                                |
| `SortableAttribute`                                                                                                           | Marks a field a caller may sort by, and the default order.   |
| `SortedQueryable`                                                                                                             | `SortedBy`, which orders a query by a sort and a tie-break.  |
| `Page`, `PageNumber`, `PageSize`                                                                                              | Offset paging and its parameters.                            |
| `Paged<T>`                                                                                                                    | A page and its total.                                        |
| `OffsetQueryable`                                                                                                             | Takes an offset page and counts only when needed.            |
| `CursorPage`, `PageAfter`                                                                                                     | Cursor paging and its parameter.                             |
| `CursorPaged<T>`                                                                                                              | A page and the cursor for the next.                          |
| `CursorQueryable`                                                                                                             | Takes a cursor page and mints the next cursor.               |
| `Fields<T>`                                                                                                                   | A sparse fieldset, `fields[type]`.                           |
| `Query`, `QueryTerm`, `QueryOperator`                                                                                         | A validated JSON Logic expression.                           |
| `QueryableQuery`                                                                                                              | `Matching`, which applies an expression to a query.          |
| `FilterableAttribute`                                                                                                         | Marks a field an expression may name.                        |
| `Search<T>`                                                                                                                   | A search's body.                                             |
| `BulkCreate<T>`, `BulkReplace<T>`                                                                                             | Bulk create and replace bodies.                              |
| `BulkTargeting<T>`                                                                                                            | How a bulk operation names its resources: `ids` or `filter`. |
| `BulkUpdate<T>`, `BulkDelete<T>`                                                                                              | Bulk update and delete bodies.                               |
| `BulkAction<T>`, `BulkAction<T, TInput>`, `BulkInput<TInput>`                                                                 | Bulk action bodies.                                          |
| `BulkResult`                                                                                                                  | What a bulk operation did.                                   |
| `BulkFailure`                                                                                                                 | Turns an exception into a bulk error.                        |
| `BulkRefusedException`                                                                                                        | An all-or-nothing bulk operation refused, with every error.  |
| `BulkRequests`                                                                                                                | Request-level checks every bulk operation makes.             |
| `Atomicity`                                                                                                                   | All-or-nothing or best-effort.                               |
| `Targeting`                                                                                                                   | Which ways of naming resources an operation accepts.         |
| `Concurrency`                                                                                                                 | Refuses a write against a stale `version`.                   |
| `InvalidQueryException`, `InvalidFilterException`, `InvalidSortException`, `InvalidCursorException`, `InvalidFieldsException` | A query parameter refused `422`.                             |

### `Bitwarden.Server.Sdk.Service.Http`

| Type                               | Purpose                                                                  |
| ---------------------------------- | ------------------------------------------------------------------------ |
| `IpRateLimitOptions`               | Per-address limits, from `IpRateLimitOptions`.                           |
| `IpRateLimitRule`                  | One limit.                                                               |
| `IpRateLimitRules`                 | Chooses which rules count a request, and in which bucket.                |
| `IpRateLimitCount`                 | One rule's count of a request.                                           |
| `DistributedIpRateLimitingOptions` | Shared counts in Redis, from `globalSettings:distributedIpRateLimiting`. |
| `IRateLimitCounters`               | Where counts are kept.                                                   |
| `MemoryRateLimitCounters`          | Counts in this process.                                                  |
| `IIpRateLimitRefusal`              | Writes the body of a refusal over a limit.                               |
| `SsrfProtectionException`          | A request refused for an internal address.                               |
| `IPAddressExtensions`              | `IsInternal()`, whether an address is in a reserved range.               |

### Other namespaces

| Type                                                                           | Purpose                                               |
| ------------------------------------------------------------------------------ | ----------------------------------------------------- |
| `HttpClientBuilderSsrfExtensions` (`Microsoft.Extensions.DependencyInjection`) | `AddSsrfProtection()` and `AddCloudSsrfProtection()`. |
| `ResolvedOnce` (`Bitwarden.Server.Sdk.Service.Caching`)                        | `GetOrResolveOnceAsync`, a cache entry resolved once. |
| `OpenApiExport` (`Bitwarden.Server.Sdk.Service.OpenApi`)                       | Writes the OpenAPI document without serving.          |
