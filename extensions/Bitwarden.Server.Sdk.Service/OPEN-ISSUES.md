# Open issues

Places where the package does not yet meet the
[API standards](https://contributing.bitwarden.com/architecture/server/apis), or behaves in a way a
service author would not expect. They were found while writing the [developer guide](docs/README.md),
which describes what the package does today and how a service works around each one.

Each entry names the standard's rule, what the package does instead, and where in the source. A fix
removes its entry and updates the guide page that describes the workaround.

## Errors where a refusal is due

- **An enumeration value in another case on a resource attribute answers `500`.** The standard reads
  enumeration values in any case. Binding checks the name ignoring case
  (`src/Resources/ResourceDocumentBinding.cs`), then the case-sensitive `JsonStringEnumConverter`
  registered in `src/Service.cs` throws. Guide: [JSON](docs/payloads-and-responses/json.md).
- **A nested object of the wrong shape, or missing a member declared `required`, answers `500`.**
  It should be `422` at the member. Binding checks only a resource's top-level attributes
  (`src/Resources/ResourceDocumentBinding.cs`), and the `JsonException` from
  `src/Resources/ResourceDocumentReading.cs` becomes a `500`. Guide:
  [Request validation](docs/payloads-and-responses/request-validation.md).
- **An `[Encrypted]` member with no `MaxLength` doesn't stop the service starting.** It fails on the
  first request that validates it (`500`), or when the OpenAPI document is written.

## Requests the standards refuse and the package accepts

- **Query parameters an operation doesn't declare are ignored, not refused `422`.** This covers an
  undeclared `filter[x]`, `sort` on an operation without `Sort`, `fields[...]` without `Fields<T>`,
  `page[...]` without paging, `page[after]` on an offset-paged list and `page[number]` on a
  cursor-paged one. Nothing checks query keys (`src/Querying/FilterBinding.cs` reads only the
  declared key). `src/Querying/Filter.cs`'s remarks say an undeclared filter is refused, which is not
  yet true. Guide:
  [Unrecognized fields, query parameters, and headers](docs/contracts/unrecognized-fields-query-parameters-and-headers.md).
- **A search refuses `?atomic=false` with a bulk-atomicity error.** `atomic` is not a search
  parameter, so it should be treated like any other unrecognized parameter. A search is declared
  all-or-nothing by default (`src/Endpoints/BulkChecks.cs`).
- **The `id` in an update's body is never compared with the path's.** The standard refuses a
  mismatch `422`. Only `type` is checked (`src/Resources/ResourceDocumentBinding.cs`,
  `ValidateResource`). A caller-sent `id` on a create also reaches the handler unchecked. Guides:
  [Updating resources](docs/operations/updating-resources.md),
  [Payload shape](docs/payloads-and-responses/payload-shape.md).
- **`version` is optional.** The standard marks it `required` where optimistic concurrency applies.
  `Resource.Version` is `string?` (`src/Resources/Resource.cs`), the OpenAPI document shows it
  optional, and `Concurrency.Require` passes a caller who sends none (`src/Querying/Concurrency.cs`).
  An empty string is dropped at binding and passes too. Guide:
  [Optimistic concurrency](docs/operations/optimistic-concurrency.md).
- **A bulk update enforces none of its rules.** Read-only fields and `version` in `update` are not
  refused, values in `update` are raw `JsonElement` and skip validation (`[StringLength]`,
  `[Encrypted(MaxLength)]`), and the version is not incremented (`src/Querying/BulkUpdate.cs`). Guide:
  [Bulk updates](docs/bulk-and-asynchronous-work/bulk-operations/bulk-updates.md).
- **A bulk replace doesn't check its items' identifiers.** Nothing checks that each item carries an
  `id`, or that none repeats. `BulkRequests.RequireDistinct` always points at `/ids/{n}`
  (`src/Querying/BulkRequests.cs`), so it can't be used on `data`. Guide:
  [Bulk replaces](docs/bulk-and-asynchronous-work/bulk-operations/bulk-replaces.md).
- **Empty strings and padding are handled only on a resource's top-level attributes.** The standard
  treats an empty string as absent and trims everywhere. Nested objects and plain JSON bodies keep
  `""` and padding, and the implied required check accepts `""` (`src/Endpoints/ModelValidation.cs`).
  The 255-character default bound also applies only to top-level string attributes.
- **Each kind of resource having its own `type` is not enforced.** Two resources may share a `type`,
  and `[ResourceType]` values aren't validated (`src/Resources/ResourceTypes.cs`).

## Shapes that differ from the standards

- **The audit fields are named `createdAt`, `updatedAt` and `deletedAt`.** The standard names them
  `creationDate`, `revisionDate` and `deletedDate` (`src/Resources/Resource.cs`). Changing them
  changes the wire format of every service. Guide:
  [Standard fields](docs/payloads-and-responses/standard-fields.md).
- **Sparse fieldsets use `fields[{type}]`.** The standard shows a plain `fields`, which the package
  ignores (`src/Querying/Fields.cs`). A cursor-paged `ReadManyResponse` also takes no `Fields<T>`
  (`src/Endpoints/StandardResponses.cs`). Guide: [Sparse fieldsets](docs/querying/sparse-fieldsets.md).
- **A bulk action with a body per resource pairs `data` with `ids` by position.** The standard wants
  `data` items of `{id, body}` with no `ids` or `filter`, and a request in that shape is refused
  (`src/Endpoints/BulkChecks.cs`, `src/Querying/BulkInput.cs`). Guide:
  [Bulk actions](docs/bulk-and-asynchronous-work/bulk-operations/bulk-actions.md).
- **`BulkRefusedException` always answers `422`.** The standard answers `409` when every failure is a
  version conflict, which an all-or-nothing bulk replace using `Concurrency.Require` produces
  (`src/Querying/BulkRefusedException.cs`). Guide:
  [All-or-nothing and best-effort](docs/bulk-and-asynchronous-work/bulk-operations/all-or-nothing-and-best-effort.md).
- **A best-effort bulk create can't return `data` with `errors`.** `BulkCreatedDocument<T>` has no
  errors and requires one result per item, and updates and replaces have no type carrying `data`
  with the counts. Guide: [Bulk responses](docs/bulk-and-asynchronous-work/bulk-operations/bulk-responses.md).
- **Malformed JSON in a plain body answers `422`, not `400`,** and the detail is the serializer's
  message, which names .NET types (`src/Endpoints/RequestBodyCheck.cs`). A search or bulk body
  missing a required member is reported the same way at `/data` rather than at the member.
- **Some error sources use the wrong member.** A header that fails is reported as `source.pointer`
  `/{header}` instead of `source.header` (`src/Endpoints/RequestRefusal.cs`), `atomic` errors use
  `source.pointer` `/atomic` instead of `source.parameter` (`src/Endpoints/BulkChecks.cs`), and a
  missing required filter points at `/data` rather than its parameter.
- **A member renamed with `[JsonPropertyName]` is reported by its C# name** in validation errors.
- **Not every error is reported at once.** Resource documents are validated a level at a time
  (`src/Resources/ResourceDocumentBinding.cs`), so a missing attribute hides an over-long one.
- **A status-only `500` has no error `id`,** though its detail asks the caller to quote one
  (`src/Resources/StatusCodeErrorWriter.cs`). Only errors from exceptions get one.
- **`InvalidRequestException` always has the title "A field has an invalid value."** whatever its
  code (`src/Resources/ErrorResponseExceptionHandler.cs`).
- **Enumerations elsewhere are read differently.** In plain JSON bodies only the exact upper snake
  case name is accepted, and undefined integers bind. In route, query and header parameters,
  ASP.NET Core's own parsing accepts C# member names and any number and refuses the upper snake case
  name (`src/Endpoints/RequestParameters.cs`). Only `Filter<T>` uses `EnumValues`.
- **`in` ignores the order of its operands.** JSON Logic puts the needle first, so
  `{"in": [{"var": "name"}, "b"]}` means "name is a substring of 'b'". The package reads a string
  literal in either position as "the field contains the literal" (`src/Querying/QueryableQuery.cs`,
  `Contains`), so a field-first `in` with a string matches the reverse of what JSON Logic says. The
  ordered comparisons (`<`, `<=`, `>`, `>=`) do honour operand order. Guide:
  [Advanced queries](docs/querying/advanced-queries.md).
- **An `and` with one term is refused with "'And' combines two or more terms",** the C# name rather
  than the JSON spelling (`src/Querying/QueryableQuery.cs`).

## Features the standards describe that the package doesn't provide

- **Deprecation.** No `deprecated` marking in the OpenAPI document and no `Deprecation`, `Sunset` or
  `Link` headers. Guide: [Deprecation](docs/contracts/deprecation.md).
- **Partial updates.** No `PATCH` attribute, and a `ResourceDocument<T>` can't tell an explicit
  `null` from an absent member, so "explicit null clears" can't be expressed. Guide:
  [Partial updates](docs/operations/partial-updates.md).
- **Asynchronous bulk work and jobs.** No `202`, job resource, `Prefer: respond-async` or
  `Preference-Applied`. Guides:
  [Synchronous and asynchronous](docs/bulk-and-asynchronous-work/bulk-operations/synchronous-and-asynchronous.md),
  [Jobs](docs/bulk-and-asynchronous-work/jobs.md).
- **Content negotiation.** `Accept` is ignored and `406` is never produced. Guide:
  [Content negotiation](docs/payloads-and-responses/content-negotiation.md).
- **Generated documentation the standards ask for.** No example JSON, a search's `[Filterable]` fields
  aren't listed, and a bulk operation's execution mode isn't documented. Guides:
  [OpenAPI](docs/contracts/openapi.md), [Bulk operations](docs/bulk-and-asynchronous-work/bulk-operations/index.md).

## Startup checks that miss a mistake

- **Operations without a standard attribute get no operation identifier, and the service still
  starts.** Only standard operations are named (`src/Endpoints/StandardEndpointConvention.cs`), and
  `src/Endpoints/StandardEndpointCheck.cs` checks only that names are unique.
- **Collection routes aren't compared with their attribute.** An attribute with an empty template,
  the default for `[StandardCreate]` and `[StandardReadMany]`, matches any route
  (`StandardEndpointCheck.Matches`). Only the verb is checked for those two.
- **Paths aren't checked.** Nothing enforces `api/v{n}/`, lowercase or kebab case on a service's own
  routes. Guide: [API paths](docs/contracts/api-paths.md).
- **Sortable declaration mistakes surface late.** A missing or repeated default, a field declared
  twice or a bad `MappedTo` throws on the first OpenAPI build, sort binding or query, not at
  startup. `SortedBy` over resources also throws when a sortable field's `MappedTo` names another
  member. Guide: [Sorting](docs/querying/sorting.md).
- **A role requirement on a domain service isn't refused at startup,** but `ICallerRole` always
  answers `None` there, so every call is refused `403` (`src/Authorization/OperationAuthorization.cs`,
  `src/Authorization/ContextRequirementsHandler.cs`). Guide:
  [Authentication and authorization](docs/running-a-service/authentication-and-authorization.md).

## Other surprises

- **`PUT` that creates gets the wrong `Location`.** A handler that sets `201` gets the request path
  plus `/{id}`, so `PUT /widgets/{id}` answers `/widgets/{id}/{id}`
  (`src/Endpoints/StandardEndpointFilter.cs`), and the OpenAPI document shows only the `200`. Guide:
  [Updating resources](docs/operations/updating-resources.md).
- **`[SelfHostedOnly]` and `[NotSelfHostedOnly]` apply only to endpoints mapped with
  `WithStandards()`, after binding and validation,** and refuse with `invalid-value`, as if a field
  were at fault (`src/Endpoints/StandardEndpointFilter.cs`). Guide:
  [Configuration and self-hosting](docs/running-a-service/configuration-and-self-hosting.md).
- **`GetOrResolveOnceAsync` needs `IMemoryCache`, which the platform doesn't register.** A service
  calls `AddMemoryCache()` itself. Guide: [Caching and extensions](docs/running-a-service/caching-and-extensions.md).
