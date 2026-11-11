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
setup call below except `WithStandards()`. A host without Service follows
[Set up a host without Service](#set-up-a-host-without-service), at the end of Usage.

## Configuration

The package reads no settings of its own. `[SelfHostedOnly]` and `[NotSelfHostedOnly]` follow
`IBitwardenEnvironment.SelfHosted`, which comes from `globalSettings:selfHosted`.

## Usage

The examples share one service whose people keep widgets. A `Widget` has a `Name` and `Notes` the
person encrypts on their device. Types named `Widget...`, such as `IWidgetStore`, are the service's
own.

### Contracts

#### Describe and lay out the API

Name each group `api`, a version, then the resource plural. Give each endpoints class an
`[ApiTag(Description = ...)]` and each handler a `<summary>` (and `<remarks>` where useful),
written for the caller. A standard attribute's default route is `{id}` for `[StandardReadOne]`,
`[StandardUpdate]` and `[StandardDelete]`, and the collection for `[StandardCreate]` and
`[StandardReadMany]`. For another route, pass a template and map the same one:

```csharp
using Bitwarden.Server.Sdk.Authorization;
using Bitwarden.Server.Sdk.Errors;
using Bitwarden.Server.Sdk.Resources;
using static Bitwarden.Server.Sdk.Resources.StandardResponses;

[ApiTag(Description = "Widgets, which a person keeps.")]
public static class WidgetsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup("api/v1/widgets").WithStandards();
        widgets.MapGet("current", GetMyWidget);
    }

    /// <summary>
    /// Get the current person's widget
    /// </summary>
    [Authorization(User = ContextRequirement.Required)]
    [StandardReadOne("current")]
    public static async Task<ResourceDocument<Widget>> GetMyWidget(
        Person person, IWidgetStore store, CancellationToken cancellationToken) =>
        ReadOneResponse(await store.FindForPersonAsync(person.Id, cancellationToken)
            ?? throw new NotFoundException("widget", "current"));

}
```

- A standard operation's identifier is the handler's name in camel case, plus the route's version
  past `v1`: `getMyWidget`, `getMyWidgetV2`. `.WithName(...)` or `[EndpointName]` wins. Renaming a
  handler renames its operation and every generated client's method.
- An endpoint without a standard attribute gets no identifier and the service still starts: name it
  with `.WithName(...)`.
- **The service refuses to start** when two operations would share an identifier (two `GetWidget`
  handlers on `v1` routes collide), when a standard endpoint is hidden with
  `ExcludeFromDescription()`, and when a handler that takes `Person` doesn't require a user.
- Read the organization from `IContext`, never a route parameter
  ([Context](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Context/src/PACKAGE.md);
  `[Authorization]` is in
  [Authorization](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Authorization/src/PACKAGE.md)).
  **A filter authorizes nothing:** a `filter[organization]` value is the caller's to choose.
- Segments the package generates are kebab case (`actions/send-email`, `:bulk-delete`). It doesn't
  check your own: to refuse a route outside `api/v{n}/` in lowercase kebab case, register an
  `IStandardEndpointCheck`, which runs over every endpoint at startup and adds to the problems.
- Schemas are named after the record (`Widget`, `WidgetDocument`). Every operation documents one
  `default` response, the error document. The package writes no examples: add them with your own
  `IOpenApiDocumentTransformer`, and never put real Vault Data in one.

#### Publish a breaking change as a new version

Map a `v2` group with its own record beside `v1`, sharing one stored shape. Put
`[ResourceType("widget")]` on the `v2` record to keep its `type` (otherwise `widget-v2`), and
`[ApiTag("Widgets")]` on both endpoints classes to keep one section; the `v1` class carries the
description.

- Version only what changed: no other `v2` path, nor a path's parents, needs to exist.
- The package doesn't detect breaking changes: commit the OpenAPI document and fail CI on a
  difference. Edits that break callers: a new non-nullable member (it is required), a nullable
  member made non-nullable, a changed type,
  a new or lower bound (a string with none is limited to 255), a member removed or made
  `[ReadOnly]`, paging added or a default page size lowered, a renamed handler, and a new
  enumeration value (an older generated client may fail to read it).

#### Accept only what an operation declares

The package drops request members the resource doesn't declare, and `[ReadOnly]` ones, before the
handler sees them. It ignores query parameters and headers the handler doesn't declare, including
a `filter[x]`, `sort`, `fields[...]` or `page[...]` the operation doesn't take, `page[after]` on an
offset-paged list and `page[number]` on a cursor-paged one. To refuse those with `422`, register an
`IStandardEndpointFilterProvider`: the package asks it once per standard endpoint and runs its
filter after binding and validation, before the handler:

```csharp
using System.Reflection;
using Bitwarden.Server.Sdk.Resources.Endpoints;
using Bitwarden.Server.Sdk.Resources.Querying;

public sealed class DeclaredQueryParameters : IStandardEndpointFilterProvider
{
    private static readonly Type[] Paging = [typeof(Sort), typeof(PageNumber), typeof(PageSize), typeof(PageAfter)];

    public IEndpointFilter? For(MethodInfo method, ICollection<string> problems)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var parameter in method.GetParameters())
        {
            var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
            var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : null;

            if (definition == typeof(Filter<>))
            {
                declared.Add($"filter[{parameter.Name}]");
            }
            else if (definition == typeof(Fields<>) || Paging.Contains(type))
            {
                declared.Add((string)(type.GetProperty("Parameter")?.GetValue(null) ?? type.GetField("Parameter")!.GetValue(null))!);
            }
        }

        return new Check(declared);
    }

    private sealed class Check(HashSet<string> declared) : IEndpointFilter
    {
        public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            foreach (var name in context.HttpContext.Request.Query.Keys)
            {
                var shapes = name == Sort.Parameter
                    || name.StartsWith("filter[", StringComparison.Ordinal)
                    || name.StartsWith("fields[", StringComparison.Ordinal)
                    || name.StartsWith("page[", StringComparison.Ordinal);

                if (shapes && !declared.Contains(name))
                {
                    throw InvalidFilterException.Because(name, $"This operation does not support '{name}'.");
                }
            }

            return next(context);
        }
    }
}
```

- Register it with `services.AddSingleton<IStandardEndpointFilterProvider, DeclaredQueryParameters>()`.
  `InvalidFilterException.Because` refuses any query parameter `422` at `source.parameter`.
- A provider that adds to `problems` stops the service at startup, listed with the other problems.
  A `[StandardReadOne]` handler whose read a registered filter answers returns
  `ServedByPlatform<T>()`, which throws if nothing answered.
- It sees only the handler's own parameters; one in an `[AsParameters]` model, or read from
  `HttpRequest.Query`, is the handler's to check. Headers of your own are yours to check.

#### Deprecate an operation

The package doesn't read `[Obsolete]`, writes no `deprecated` flag and adds no `Deprecation`,
`Sunset` or `Link` headers. Put an attribute of your own on the handler or its endpoints class (the
package copies a class's attributes onto each standard endpoint, so it reaches the metadata), add
the headers from middleware outside the package's with `Response.OnStarting` so refusals carry
them, and set `Deprecated` from an `IOpenApiDocumentTransformer`. Nothing watches the sunset date.

### Payloads and responses

#### Declare a resource

Derive a record from `Resource`. Nothing in it mentions `data` or `attributes`:

```csharp
using System.ComponentModel.DataAnnotations;
using Bitwarden.Server.Sdk.Data;
using Bitwarden.Server.Sdk.Resources;

public sealed record Widget : Resource
{
    [StringLength(100)]
    public required string Name { get; init; }

    // Protected Data, encrypted on the person's device: stored as it arrives.
    [Encrypted(MaxLength = 10_000)]
    public string? Notes { get; init; }

    // Set by the service; a caller's value is dropped.
    [ReadOnly]
    public int PolishCount { get; init; }

    // Kept exactly as sent, spaces and all.
    [Verbatim]
    [StringLength(40)]
    public string? Engraving { get; init; }
}
```

- `Resource` gives `Id` and `Version` (both `string?`), `Type`, and the read-only `CreatedAt`,
  `CreatedBy`, `UpdatedAt`, `UpdatedBy`, `DeletedAt` and `DeletedBy`. The package sets none of
  them; a `null` member is left out of the response.
- `type` is the record's name in kebab case (`ApiKey` is `api-key`, `HTTPServer` is `http-server`),
  or `[ResourceType]`'s value as written. **Each kind of resource needs its own `type`;** nothing
  checks that two don't share one, and two records of the same name in different namespaces do.
- Members are camel case on the wire and in filter, sort and fieldset names. Avoid
  `[JsonPropertyName]`: validation errors still use the C# name.
- **`[ReadOnly]`** (this package's, not `System.ComponentModel`'s) drops a caller's value before
  validation, so a caller can send back what it read. Mark every member only the service sets.
- Top-level string attributes are trimmed and a blank one is absent (a required one is refused
  `field-required`). **`[Verbatim]`** keeps the string as sent; its validation still runs.
- **`[Encrypted(MaxLength = ...)]`** (Bitwarden.Server.Sdk.Data.Abstractions, namespace
  `Bitwarden.Server.Sdk.Data`) checks only the length;
  `WellFormed = true` also refuses a value not shaped like an encrypted string. Give ciphertext a
  larger bound than its text. Never decrypt, log, filter or sort on it.
- A `JsonElement` or `JsonNode` member takes any JSON value unchecked: check its kind yourself.
- Enumerations are written in upper snake case (`SPECIAL_EDITION`), and their member names are part
  of your contract. `Filter<T>` reads them in any case, a resource attribute only exactly; elsewhere
  take a `string` and read it with `EnumValues.TryParse`, which matches the published names in any
  case.

#### Take and return documents

Handlers take a `ResourceDocument<T>` and return a document built by a `StandardResponses` helper:

| Operation            | Return                                                           | Status                                      |
| -------------------- | ---------------------------------------------------------------- | ------------------------------------------- |
| `[StandardCreate]`   | `CreateResponse(resource)`                                       | `201`, with `Location` from the path and id |
| `[StandardReadOne]`  | `ReadOneResponse(resource)` or `(resource, fields)`              | `200`                                       |
| `[StandardReadMany]` | `ReadManyResponse(...)`: a list, an offset page or a cursor page | `200`, with paging `meta` and links         |
| `[StandardUpdate]`   | `UpdateResponse(resource)`                                       | `200`                                       |
| `[StandardAction]`   | `ActionResponse(resource)`, or your own value                    | `200`                                       |
| Any operation        | Nothing (`Task`)                                                 | `204`                                       |

- Each helper takes an optional `meta`, left out when empty. A handler that returns an `IResult`, or
  sets the status itself, keeps it.
- A body without `type` is `422` `field-required`, another type `422` `invalid-resource-type`, at
  `/data/type`. A body that isn't JSON is `400` `malformed-json`; one without `data` is `422`.
- **Check the `id` yourself:** on an update, as `UpdateWidget` under
  [Map standard operations](#map-standard-operations) does; on a create, a caller's `id` reaches
  `request.Data.Id`, so refuse or ignore it.
- Any other body parameter (an action's arguments, a search, a bulk body) is plain JSON with no
  envelope. One that isn't JSON is `422` with the serializer's message, not `400` `malformed-json`.
- A resource document takes `application/json` or a `+json` type, a plain body `application/json`,
  `text/json` or `application/*+json`; anything else is `415`. A plain body in another media type
  is refused after authorization, so a caller without the scope gets `403` first. A raw body takes `HttpRequest` and
  names its type with `[Consumes]`; pass an encrypted upload to storage unread.
- **Every response is JSON.** `Accept` is ignored and `406` never produced. To answer `406`, return
  `Results.StatusCode(406)` from an `IStandardEndpointFilterProvider`'s filter; the package writes
  the error document.

#### Validate a request

The package binds and validates every request before the handler runs, from parameter types,
nullability and DataAnnotations. The handler checks only what needs data, such as a name already
taken, with `InvalidRequestException` at a pointer and a code like `ErrorCodes.AlreadyExists`:

```csharp
using System.ComponentModel.DataAnnotations;
using Bitwarden.Server.Sdk.Resources;
using static Bitwarden.Server.Sdk.Resources.StandardResponses;

// Nullable and [Required], so a body without it is refused rather than read as 0.
public sealed record PolishWidget([property: Required, Range(1, 10)] int? Shine);

public static class WidgetsEndpoints
{
    [StandardAction]
    public static async Task<ResourceDocument<Widget>> Polish(
        string id, PolishWidget request, IWidgetStore store, CancellationToken cancellationToken) =>
        ActionResponse(await store.PolishAsync(id, request.Shine!.Value, cancellationToken));
}
```

- A non-nullable reference member, a non-nullable resource attribute and a non-nullable parameter
  without a default are required. In a plain body an absent `int` or `bool` binds as `0` or
  `false`: declare it nullable and `[Required]`, as above. Every DataAnnotations attribute runs, on nested
  objects and list items too, then `IValidatableObject.Validate` once every member passes. The same
  holds for `[FromForm]` and `[AsParameters]` models.
- Each error points at what failed (`/data/attributes/part/code`, or `source.parameter`), up to 200.
- **Bound every string.** The 255-character default, trimming and blank-as-absent apply only to a
  resource's top-level strings. Elsewhere add `[StringLength]`, trim yourself, and declare
  `[Required]` to refuse `""`.
- **`[Encrypted]` needs a positive `MaxLength`,** or validating the resource and writing the
  OpenAPI document fail with `500`; the service still starts.
- A resource document is checked a level at a time, so an absent `name` hides an over-long `notes`.
- `[AcceptsInvalidRequests]` runs the handler even when binding or validation failed, for a route
  whose existing clients expect their own answers. An unreadable body binds as `null`, its bytes in
  the `UnreadableRequestBody` feature.

#### Report a failure

Throw an exception from Bitwarden.Server.Sdk.Errors and the package writes the error document. The
exceptions, their statuses, `IProvideErrors` and `IExceptionTranslator` are in
[Errors](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Errors/src/PACKAGE.md).
What this package adds:

- **Any other exception is a `500` that says nothing.** It is logged at `Error` with the request's
  trace identifier, which the caller gets as the error's `id`. Every other detail you write reaches
  the caller: never put Vault Data, ciphertext, a secret or an internal address in one.
- When an `IExceptionTranslator` turns an exception into another, the log keeps the original.
- `404` when the caller may not know the resource exists, `403` when they may know but not act. A
  resource named in the path that isn't there is `404`; one named in the body is `422` at its
  pointer.
- Routing decides `404`, `405` and a resource document's `415` before authentication, so a caller
  without a valid token gets `401` first.
- `[SelfHostedOnly]`, on a handler or its class, refuses `422` "Only allowed when self hosted." in
  the cloud, and `[NotSelfHostedOnly]` "Only allowed when not self hosted." in a self-hosted
  install: an invalid-value error that names no field, after binding and validation. Both apply
  only under `WithStandards()`; elsewhere, check `IBitwardenEnvironment.SelfHosted` yourself.
- A header parameter that fails is reported at `source.pointer` `/{header}`, not `source.header`.
  To name it, take the header as a `string`, check it in the handler, and throw an `IProvideErrors`
  exception whose error sets `Source = new ErrorSource { Header = name }`.

### Operations

#### Map standard operations

Map the group with `WithStandards()`, and give each handler one `Map...` line and one attribute:

```csharp
using Bitwarden.Server.Sdk.Errors;
using Bitwarden.Server.Sdk.Resources;
using static Bitwarden.Server.Sdk.Resources.StandardResponses;

public static class WidgetsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup("api/v1/widgets").WithStandards();
        widgets.MapPost("", CreateWidget);
        widgets.MapGet("{id}", GetWidget);
        widgets.MapPut("{id}", UpdateWidget);
    }

    /// <summary>
    /// Create a widget
    /// </summary>
    [StandardCreate]
    public static async Task<ResourceDocument<Widget>> CreateWidget(
        ResourceDocument<Widget> request, IWidgetStore store, CancellationToken cancellationToken) =>
        CreateResponse(await store.AddAsync(request.Data, cancellationToken));

    [StandardReadOne]
    public static async Task<ResourceDocument<Widget>> GetWidget(
        string id, IWidgetStore store, CancellationToken cancellationToken) =>
        ReadOneResponse(await store.FindAsync(id, cancellationToken) ?? throw new NotFoundException("widget", id));

    [StandardUpdate]
    public static async Task<ResourceDocument<Widget>> UpdateWidget(
        string id, ResourceDocument<Widget> request, IWidgetStore store, CancellationToken cancellationToken)
    {
        // The package checks the body's type, never its id.
        if (request.Data.Id is { } sent && sent != id)
        {
            throw new InvalidRequestException($"The id '{sent}' is not the widget in the path.", "/data/id");
        }

        return UpdateResponse(await store.ReplaceAsync(id, request.Data, cancellationToken)
            ?? throw new NotFoundException("widget", id));
    }
}
```

- `[StandardCreate]` is `MapPost` at `""`, `[StandardReadMany]` `MapGet` at `""`,
  `[StandardReadOne]` `MapGet` at `{id}`, `[StandardUpdate]` `MapPut` at `{id}`,
  `[StandardDelete]` `MapDelete` at `{id}`, `[StandardAction]` `MapPost` at
  `{id}/actions/{action}`. A verb nothing is mapped for is `405`.
- **The service refuses to start** when a handler is mapped with another verb or route than its
  attribute's: `'WidgetsEndpoints.GetWidget' is [StandardReadOne], which is GET, but is mapped with POST.`
  `[StandardCreate]` and `[StandardReadMany]` match any route, so only their verb is checked.
- Reads demand the service's read scope and other operations the write scope, unless they declare
  their own. The package authorizes the operation, not the rows: limit a collection to what the
  caller may see. Keep `GET` handlers free of side effects. There is no `202` for scheduled work.
- **Create.** `Location` is the request path plus `Id`; pass `CreateResponse` a `location` when the
  resource is read elsewhere. Set the audit fields yourself (`IContext.UserId` is the acting user).
- **Read.** A list that finds nothing is an empty `200`. Page anything that grows.
- **Delete** returns nothing, for `204`; throw `NotFoundException` when there is no such resource.
- **Update** is a full replace: an attribute left out is absent. For a `PUT` that creates, set `201`
  on `HttpContext.Response` and always pass the request path as `location`; the derived one repeats
  the `id`. The OpenAPI document shows only the `200`.
- **Soft delete.** Take `[FromQuery] bool permanent = false` on the delete and `includeDeleted` on
  reads, filter in your store, and set `DeletedAt` and `DeletedBy`. Those flags are open to anyone
  with the scope: give the operation its own `[Authorization]` where they shouldn't be. Restore with
  an action. Ciphertext in the trash stays ciphertext until your retention rule removes it.

#### Act on a resource

An operation with its own rules or permission, such as a status change or sending an email, is an
action:

```csharp
using Bitwarden.Server.Sdk.Errors;
using Bitwarden.Server.Sdk.Resources;

public static class WidgetsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("api/v1/widgets").WithStandards().MapPost("{id}/actions/archive", ArchiveWidget);

    [StandardAction("archive")]
    public static async Task ArchiveWidget(string id, IWidgetStore store, CancellationToken cancellationToken)
    {
        if (!await store.ArchiveAsync(id, cancellationToken))
        {
            throw new InvalidRequestException("Only an active widget can be archived.", code: "invalid-state");
        }
    }
}
```

- The action's name is the method's, or the one passed, kebab-cased (`VerifySSOToken` is
  `verify-sso-token`). The operation identifier is still the method's (`archiveWidget`).
- `template` names the resource it acts on, `{id}` by default:
  `[StandardAction("reset", template: "current")]` maps `current/actions/reset`.
- Arguments are a plain JSON body bound to your record and validated; no body parameter, no body.
  Return `ActionResponse(resource)` for `200`, or nothing for `204`. Declare any argument that is
  Vault Data `[Encrypted]` on the record, store it as it came, and never decrypt or log it.
- Mark a member only actions change `[ReadOnly]`. A transition not allowed from the current state
  is `422` with a code callers can branch on; a race with another write is `409`.

#### Change part of a resource

There is no `PATCH` attribute, and a `ResourceDocument<T>` can't tell a member sent as `null` from
one left out. Change one member with an action, or several by replacing with `PUT`. If you must
offer `PATCH`, map it with `MapPatch` and `.WithName(...)` in a `WithStandards()` group and take a
`JsonObject`. It gets the error document and the write scope, but no `type` or `id` check and no
resource schema: every check on the body is the handler's.

#### Detect conflicting updates

Return `version` in every response, check the one the caller sends back, and write only if it is
unchanged:

```csharp
using Bitwarden.Server.Sdk.Errors;
using Bitwarden.Server.Sdk.Resources;
using Bitwarden.Server.Sdk.Resources.Querying;
using static Bitwarden.Server.Sdk.Resources.StandardResponses;

[StandardUpdate]
public static async Task<ResourceDocument<Widget>> UpdateWidget(
    string id, ResourceDocument<Widget> request, IWidgetStore store, CancellationToken cancellationToken)
{
    // The package passes a caller who sends no version. Refuse one to require it.
    if (request.Data.Version is null)
    {
        throw new InvalidRequestException(
            ErrorDetails.FieldRequired("version"), "/data/attributes/version", ErrorCodes.FieldRequired);
    }

    var stored = await store.FindRowAsync(id, cancellationToken) ?? throw new NotFoundException("widget", id);
    Concurrency.Require(request.Data.Version, stored.Version);

    // The store writes only if the row is still at stored.Version, and adds one to it.
    return UpdateResponse(await store.ReplaceAsync(id, request.Data, stored.Version, cancellationToken)
        ?? throw new ConflictException());
}
```

- `Concurrency.Require(sent, stored)` compares the sent string with an `int` version and throws
  `ConflictException` (`409`) on a mismatch.
- `version` is optional in binding and in the OpenAPI document, and `""` counts as absent.
- The package doesn't increment the version, set `ETag` or read `If-Match`. Make the write
  conditional (a concurrency token, or `WHERE Version = @expected`) so two writers can't both win.

### Querying

#### Filter, sort and page a list

Each query parameter is an ordinary handler parameter. Mark sortable fields on the resource:

```csharp
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Bitwarden.Server.Sdk.Resources;
using Bitwarden.Server.Sdk.Resources.Querying;
using Microsoft.EntityFrameworkCore;
using static Bitwarden.Server.Sdk.Resources.StandardResponses;

[Sortable(nameof(CreatedAt), Default = true, Descending = true)]
[Sortable(nameof(UpdatedAt), MappedTo = nameof(WidgetEntity.ModifiedAt))]
public sealed record Widget : Resource
{
    [Sortable]
    [StringLength(100)]
    public required string Name { get; init; }
}

public static class WidgetsEndpoints
{
    [StandardReadMany]
    public static async Task<PagedResourceListDocument<Widget>> ListWidgets(
        Filter<List<Guid>>? id,
        Filter<Range<DateTimeOffset>>? createdAt,
        Sort? sort,
        WidgetDatabase database,
        PageNumber pageNumber = default,
        [DefaultValue(25)] PageSize pageSize = default,
        CancellationToken cancellationToken = default)
    {
        var page = new Page { Number = pageNumber, Size = pageSize };
        var widgets = database.Widgets.AsNoTracking().Within(widget => widget.CreatedAt, createdAt?.Value);

        if (id is not null)
        {
            widgets = widgets.Where(widget => id.Value.Contains(widget.Id));
        }

        // The caller's order or the default, then the identifier, so a page is stable.
        var rows = await widgets
            .SortedBy(sort ?? Sort.Default<Widget>(), widget => widget.Id)
            .Page(page)
            .ToListAsync(cancellationToken);

        // Counts only when the rows can't settle the total.
        return ReadManyResponse(await rows.ToPagedAsync(page, ToResource, widgets.CountAsync, cancellationToken), page);
    }

    private static Widget ToResource(WidgetEntity entity) =>
        new() { Id = entity.Id.ToString(), Name = entity.Name, CreatedAt = entity.CreatedAt };
}
```

- **Filters.** `Filter<T>? name` reads `filter[name]`; nullable is optional, non-nullable required.
  A list `T` takes `a,b` or the parameter repeated (`\,` escapes a comma), and drops a value that
  doesn't convert. A single value is taken whole; one that doesn't convert, or sent twice, is `422`.
  No dot notation, and no wildcards: a `*` is a literal.
- **Ranges.** `Filter<Range<T>>` reads `[2026-01-15T00:00:00Z,*)`: `[`/`]` include, `(`/`)` exclude,
  `*` is open. `Within` applies it in the database. To limit one, read `Lower` and `Upper` and throw
  `InvalidFilterException`.
- **Sorting.** `sort=-updatedAt,name` accepts only `[Sortable]` fields, in any case; anything else is
  `422`, listing them. `SortedBy` runs in the database: always pass a unique tie-break.
  `Sort.By(...)` builds one in code, naming entity properties; for a computed field read
  `sort.Fields` and order the query yourself.
- **Sortable declarations throw late.** No default, two defaults, a field declared twice or a bad
  `MappedTo` throws on the first OpenAPI build, sort binding or sorted query, not at startup. Test a
  sorted list of each resource.
- **Page numbers.** `page[number]` defaults to 1, `page[size]` to 50 or the parameter's
  `[DefaultValue]`; a size above 200 (`Page.MaximumSize`) becomes 200, below 1 is `422`. The links
  repeat the caller's filters, sort and fieldset. Pass `atMost` to `ToPagedAsync` only for a bound a
  database key enforces. `ToPaged(page)` pages a list in memory.
- **Declare only the filters and sort fields callers need,** each indexed. Never filter or sort on
  an `[Encrypted]` member: its ciphertext means nothing to the server.

#### Page by cursor

For a large or fast-moving collection read in order, page by cursor. It never counts:

```csharp
using Bitwarden.Server.Sdk.Resources;
using Bitwarden.Server.Sdk.Resources.Querying;
using Microsoft.EntityFrameworkCore;
using static Bitwarden.Server.Sdk.Resources.StandardResponses;

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

    // Filters go before Page, which orders the query and reads one row more than the page holds.
    var rows = await database.WidgetEvents.AsNoTracking().Page(page, order, row => row.Id).ToListAsync(cancellationToken);

    return ReadManyResponse(rows.ToCursorPage(page, order, row => row.Id, ToResource), page);
}
```

- The cursor is opaque. `meta` carries the next one, which the caller sends as `page[after]`; there
  are no links. A cursor from another operation or sort is `422`. It records the sort, not filters.
- Pass the same sort and unique tie-break to `Page` and `ToCursorPage`. A nullable sort field or
  tie-break makes `Page` throw `InvalidOperationException`.
- A cursor-paged `ReadManyResponse` takes no `Fields<T>`.

#### Return only the fields asked for

Take `Fields<Widget> fields = default` and pass it to `ReadOneResponse(resource, fields)` or
`ReadManyResponse(page, requested, fields)`. Check `fields.Includes("gadgetCount")` before
producing an attribute that costs something.

- The parameter is `fields[widget]`, keyed by `type`; a plain `fields` is ignored. A name that isn't
  a published attribute is `422`.
- No fieldset means every attribute, so adding `Fields<T>` changes nothing for existing callers. An
  empty one returns only `id` and `type`, which are always returned.

#### Search with a filter expression

When `filter[...]` can't say enough ("or", or too long for a URL), offer a search: a `POST` to
`{collection}:search` whose body holds a JSON Logic expression over the fields the resource marks
`[Filterable]` (`[Filterable(nameof(CreatedAt))]` on the record for an inherited one, `MappedTo` for
another entity name):

```csharp
using Bitwarden.Server.Sdk.Resources;
using Bitwarden.Server.Sdk.Resources.Querying;
using Microsoft.EntityFrameworkCore;
using static Bitwarden.Server.Sdk.Resources.StandardResponses;

public static class WidgetsEndpoints
{
    public const string Collection = "api/v1/widgets";

    // On the route builder, not a group: the path is the collection's plus a suffix.
    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost($"{Collection}:search", SearchWidgets).WithStandards();

    [StandardSearch(Collection)]
    public static async Task<PagedResourceListDocument<Widget>> SearchWidgets(
        Search<Widget> request,
        WidgetDatabase database,
        PageNumber pageNumber = default,
        PageSize pageSize = default,
        CancellationToken cancellationToken = default)
    {
        var page = new Page { Number = pageNumber, Size = pageSize };

        // Checked against the [Filterable] fields before the handler ran.
        var widgets = database.Widgets.AsNoTracking().Matching(request.Filter);
        var rows = await widgets.SortedBy(Sort.Default<Widget>(), widget => widget.Id).Page(page).ToListAsync(cancellationToken);

        return ReadManyResponse(await rows.ToPagedAsync(page, ToResource, widgets.CountAsync, cancellationToken), page);
    }

    private static Widget ToResource(WidgetEntity entity) => new() { Id = entity.Id.ToString(), Name = entity.Name };
}
```

- The body's `filter` is required and checked first; every problem is `422` at `/filter`. Allowed:
  `var`, `==`, `!=`, `>`, `>=`, `<`, `<=`, `in`, `and`, `or`, `!`, at most 500 terms
  (`Query.MaxTerms`), each comparison one field against one literal. Ordered comparisons need
  numbers or dates. `in` is membership in an array, or a substring match against a string.
- `sort`, `page[...]` and `fields[...]` bind as on a list. Page links carry only the query string, so
  a caller follows one with the same body. Share one read with the list so both answer alike.
- `Matching` works on any `IQueryable`. It throws `InvalidOperationException` for an expression that
  didn't arrive in a `Search<T>` or bulk body, and on first use for a filterable field with no entity
  property. A handler that takes `Search<T>` or a `Query` anywhere but on a search or bulk
  operation in a `WithStandards()` group stops the service at startup: its filter would reach the
  handler unchecked.
- The package bounds an expression's size, not its cost: refuse an expensive one with
  `InvalidRequestException` at `/filter`. Mark only indexed fields callers may see, never an
  `[Encrypted]` one.

### Bulk work

#### Declare bulk operations

Each bulk operation is a `POST` to a suffix on the collection, mapped on the route builder:

| Attribute                                    | Request type                               | Route                        |
| -------------------------------------------- | ------------------------------------------ | ---------------------------- |
| `[StandardBulkCreate(collection)]`           | `BulkCreate<T>`                            | `{collection}:bulk-create`   |
| `[StandardBulkUpdate(collection)]`           | `BulkUpdate<T>`                            | `{collection}:bulk-update`   |
| `[StandardBulkReplace(collection)]`          | `BulkReplace<T>`                           | `{collection}:bulk-replace`  |
| `[StandardBulkDelete(collection)]`           | `BulkDelete<T>`                            | `{collection}:bulk-delete`   |
| `[StandardBulkAction(collection, "action")]` | `BulkAction<T>` or `BulkAction<T, TInput>` | `{collection}:bulk-{action}` |

```csharp
using Bitwarden.Server.Sdk.Context;
using Bitwarden.Server.Sdk.Errors;
using Bitwarden.Server.Sdk.Resources;
using Bitwarden.Server.Sdk.Resources.Querying;

public static class WidgetsEndpoints
{
    public const string Collection = "api/v1/widgets";

    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost($"{Collection}:bulk-delete", DeleteWidgets).WithStandards();

    /// <summary>
    /// Delete widgets
    /// </summary>
    /// <remarks>
    /// Names the widgets by `ids`, at most 500. All-or-nothing, or best-effort with `atomic=false`.
    /// Responds synchronously.
    /// </remarks>
    [StandardBulkDelete(Collection, Targeting = Targeting.Ids, Supports = Atomicity.AllOrNothing | Atomicity.BestEffort)]
    public static async Task<BulkResult> DeleteWidgets(
        BulkDelete<Widget> request, IContext context, IWidgetStore store, CancellationToken cancellationToken)
    {
        // Problems with the request as a whole refuse it before any widget is touched.
        var ids = request.Ids ?? [];
        BulkRequests.RequireAtMost(ids.Count, 500, "/ids");
        BulkRequests.RequireDistinct(ids);

        var failures = new List<Error>();
        var deletable = new List<string>();

        for (var index = 0; index < ids.Count; index++)
        {
            // From what the caller may see: a hidden widget fails as a missing one does.
            if (await store.FindVisibleAsync(context, ids[index], cancellationToken) is { } widget)
            {
                deletable.Add(widget.Id!);
            }
            else
            {
                failures.Add(BulkFailure.At(new NotFoundException("widget", ids[index]), $"/ids/{index}"));
            }
        }

        if (request.Atomicity == Atomicity.AllOrNothing && failures.Count > 0)
        {
            throw new BulkRefusedException(failures);
        }

        var deleted = await store.DeleteAllAsync(deletable, cancellationToken);

        return request.Atomicity == Atomicity.AllOrNothing
            ? BulkResult.Affected(deleted)
            : BulkResult.BestEffort(deleted, failures);
    }
}
```

- **Targeting.** Updates, deletes and actions take `ids` or a `filter` expression (the search's
  language), as `Targeting` allows (`Either` by default). Both, neither, an excluded form or an
  empty `filter` is `422`, so a bulk delete never means "everything". An empty `ids` reaches the
  handler. Creates and replaces take `data`.
- **Atomicity.** `Supports` offers `AllOrNothing` (the default), `BestEffort` or both; the caller
  picks with `atomic=true|false`, and `request.Atomicity` holds the checked choice.
- **Limits are yours.** There is no default maximum: call `BulkRequests.RequireAtMost` first.
  `RequireDistinct` refuses a repeated id, ignoring case, at `/ids/{n}`. `RequireAtMost` bounds
  `ids` only: a `filter` can match the whole collection the caller may see, and every match is
  checked before anything is written. Cap the matches yourself (refuse at `/filter` when there are
  more than your maximum), or accept `Targeting.Ids` only.
- **The service refuses to start** when a bulk route disagrees with its attribute, a handler takes
  `BulkUpdate<T>`, `BulkDelete<T>` or `BulkAction<T>` without a bulk attribute (its filter would
  reach the handler unchecked), or a bulk action is named `create`, `update`, `replace` or
  `delete`.
- **The caller's reach is yours.** Start every lookup from what the caller may see (`IContext`), so
  targeting never reaches further than single operations would.
- **Everything is synchronous:** no `202`, no job resource, and `Prefer: respond-async` is ignored.
  Choose a maximum the slowest request finishes within, and write it and "Responds synchronously"
  in `<remarks>`; the package documents neither. Don't design a job shape of your own; where the
  later work is another service reacting, publish an event.

#### Apply each resource's rules and report failures

A bulk operation does to each resource what the single operation would: the same authorization,
validation, business rules and side effects (events, audit records). The package checks only the
request as a whole, so share one implementation between the two.

- `BulkFailure.At(exception, pointer)` turns the exception the single operation would throw into
  an error with its status, code and detail. An unrecognized one becomes a `500` without its
  message, so catch only per-resource failures. Point at `/ids/{n}`, `/data/{n}` or deeper.
- A resource a filter matched has no position: set `Source = null` and
  `Meta = { ["resource"] = new FailedResource("widget", id) }` on its error.
- All-or-nothing: throw `BulkRefusedException(failures)`, `422` with every failure. The package
  opens no transaction: check every resource before writing any, then write in one transaction.
- Best-effort: return `BulkResult.BestEffort(affected, failures)`, `200` with `affectedCount`,
  `failedCount` and the errors. Don't return nothing from one; the caller never learns what failed.
- `BulkRefusedException` always answers `422`. For `409` when every failure is a conflict, throw
  your own `IProvideErrors` exception with `StatusCode` `409`.

#### Bulk create, update, replace and action

- **Create.** Each item is read and validated as a single create's resource and may carry a `lid`;
  one that isn't a non-empty string, or is shared, is `422`. `BulkCreatedDocument<T>.For` answers with the created resources in request order, each `lid` echoed, and throws unless
  there is one per item. A best-effort create can only return `BulkResult.BestEffort`, without
  identifiers.
- **Update.** `request.Update` is the change as sent, an `IReadOnlyDictionary<string, JsonElement>`
  keyed by published names, an explicit `null` as `JsonValueKind.Null`. None of the single update's
  rules apply to it: refuse read-only fields and `version` at `/update/{field}`, check each value as
  the resource's annotations would (only the length of an encrypted one), and increment the version
  of each resource that changes. A status change for many resources is a bulk action.
- **Replace.** Nothing checks that each item carries an `id` or that none repeats, and
  `RequireDistinct` can't point into `data`: refuse at `/data/{n}/id` yourself. Call
  `Concurrency.Require` per item and increment versions. A missing resource is a `404` failure of
  its item, unless the single `PUT` creates.
- **Action.** `request.Data` is a `BulkInput<TInput>`: a JSON object is one body for every resource,
  an array one per resource paired with `ids` by position; `At(index)` returns the right one. An
  array with a `filter`, without `ids` or of another length is `422`. Items carrying their own `id`
  and `body` aren't read. `data` is optional: refuse its absence if the action needs it.
  Per-resource bodies are for input only the client can produce, such as a key it wraps per user.
- **Delete.** Soft or hard follows the single delete; declare `permanent` as a handler parameter.
- Protected Data in any bulk body passes through as sent and is never read.

### Set up a host without Service

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
- Turn on `EnableRequestDelegateGenerator` and `GenerateDocumentationFile`, and add
  `Microsoft.AspNetCore.OpenApi.Generated` to `InterceptorsNamespaces`, so handlers compile ahead of
  time and their XML comments reach the OpenAPI document.
