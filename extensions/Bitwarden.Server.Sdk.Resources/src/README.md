# Bitwarden.Server.Sdk.Resources

What a Bitwarden service's API looks like to its callers: resource documents, standard operations
and their checks, the error document, querying, bulk operations and their OpenAPI description. See
[PACKAGE.md](PACKAGE.md) for using it, and the
[developer guide](../../Bitwarden.Server.Sdk.Service/docs/README.md) for writing a service with it.

## What belongs here

Anything that shapes what a caller sends, receives or reads in the OpenAPI document. Hosting,
authentication, telemetry, the serializer settings and the general OpenAPI setup belong in
Bitwarden.Server.Sdk.Service; error types a service and its callers both need belong in
Bitwarden.Server.Sdk.Errors.

- **Entry points stay few and explicit:** `AddStandardErrors`, `AddStandardEndpoints`,
  `UseStandardStatusCodePages`, `WithStandards`, `RequireStandards` and `DescribeResources`. Service
  calls them for every service. A new behaviour hangs off one of these rather than asking each
  service for another call.
- **Zero knowledge.** `[Encrypted]` checks an encrypted value's length and shape and nothing else.
  Nothing here decrypts, logs or inspects an encrypted value, and no test fixture holds plaintext
  Vault Data.

## How it works

A standard endpoint behaves as the same operation written as an MVC controller action would: the
same binding, validation, refusal wording and OpenAPI description. `WithStandards()` applies
`StandardEndpointConvention` to each endpoint a group maps, which adds the metadata, the success
status and `StandardEndpointFilter`, the filter that does what model binding, validation and action
filters do around a controller action.

| File                                                           | What it does                                                                        |
| -------------------------------------------------------------- | ----------------------------------------------------------------------------------- |
| `Endpoints/StandardEndpointFilter.cs`                          | Binds and validates, runs the self-hosted and bulk checks, sets status and Location |
| `Endpoints/StandardEndpointCheck.cs`                           | The startup check behind `RequireStandards()`, naming every problem at once         |
| `Endpoints/RequestBodyCheck.cs`                                | Reads a plain JSON body before the framework, to refuse it in MVC's words           |
| `Endpoints/ModelValidation.cs`                                 | DataAnnotations validation as MVC runs it, the 255 default and the error cap        |
| `ResourceDocumentReading.cs`, `ResourceDocumentBinding.cs`     | Reads a resource document and validates it before a service sees any of it          |
| `ResourceDocumentJsonConverterFactory.cs`                      | Applies the `attributes` envelope, by reflection (so the package isn't AOT-safe)    |
| `Endpoints/StandardEndpointApiDescriptionProvider.cs`          | Describes each standard operation as a controller action's would be described       |
| `OpenApi/`                                                     | The transformers `DescribeResources()` registers                                    |
| `ErrorResponseExceptionHandler.cs`, `StatusCodeErrorWriter.cs` | Exceptions and bare status codes become the error document                          |
| `Querying/QueryableQuery.cs`                                   | Turns a checked filter expression into a predicate a database can run               |

- **A declaration the package can check when endpoints are built is checked there,** through
  `RequireStandards()`, rather than on the first request.
- **Handlers behave the same with and without the request delegate generator.** The generated code
  and `RequestDelegateFactory` differ at the edges, so both paths are tested (see Testing).

## Known limitations

Where the package falls short of the
[API standards](https://contributing.bitwarden.com/architecture/server/apis) or would surprise a
service author. The developer guide gives the workaround where there is one; a fix removes its
entry.

**Requests the standards refuse and the package accepts**

- Undeclared query parameters (`filter[x]`, `sort`, `fields[...]`, `page[...]`, and the wrong
  paging style) are ignored, not refused `422`: nothing checks query keys
  (`Querying/FilterBinding.cs` reads only the declared key).
- A search refuses `?atomic=false` with a bulk-atomicity error instead of ignoring it: a search is
  declared all-or-nothing (`Endpoints/BulkChecks.cs`).
- An update's body `id` is never compared with the path's, and a create's reaches the handler;
  only `type` is checked (`ResourceDocumentBinding.cs`, `ValidateResource`).
- `version` is optional where optimistic concurrency applies: `Resource.Version` is `string?`, the
  OpenAPI document shows it optional, and `Concurrency.Require` passes none or `""`
  (`Resource.cs`, `Querying/Concurrency.cs`).
- A bulk update enforces none of its rules: read-only fields and `version` in `update` pass, values
  are raw `JsonElement` and skip validation, and the version isn't incremented
  (`Querying/BulkUpdate.cs`).
- A bulk replace doesn't check that items carry an `id` or that none repeats, and
  `BulkRequests.RequireDistinct` always points at `/ids/{n}` (`Querying/BulkRequests.cs`).
- Empty strings, trimming and the 255-character default apply only to a resource's top-level
  attributes; nested objects and plain bodies keep `""` and padding, and the implied required check
  accepts `""` (`Endpoints/ModelValidation.cs`).
- Two resources may share a `type`, and `[ResourceType]` values aren't validated
  (`ResourceTypes.cs`).
- An `[Encrypted]` member without `MaxLength` doesn't stop startup; it fails with `500` on the
  first validation or OpenAPI build.

**Shapes that differ from the standards**

- The audit fields are `createdAt`, `updatedAt` and `deletedAt`, not `creationDate`,
  `revisionDate` and `deletedDate` (`Resource.cs`). Changing them changes every service's wire
  format.
- Sparse fieldsets use `fields[{type}]`; a plain `fields` is ignored (`Querying/Fields.cs`). A
  cursor-paged `ReadManyResponse` takes no `Fields<T>` (`Endpoints/StandardResponses.cs`).
- A bulk action with a body per resource pairs `data` with `ids` by position; `data` items of
  `{id, body}` are refused (`Endpoints/BulkChecks.cs`, `Querying/BulkInput.cs`).
- `BulkRefusedException` always answers `422`, never `409` when every failure is a version conflict
  (`Querying/BulkRefusedException.cs`).
- A best-effort bulk create can't return `data` with `errors` (`BulkCreatedDocument.cs`), and
  updates and replaces have no type carrying `data` with the counts.
- Malformed JSON in a plain body answers `422`, not `400`, with the serializer's message, which
  names .NET types (`Endpoints/RequestBodyCheck.cs`). A search or bulk body missing a required
  member is reported at `/data` rather than at the member.
- Some error sources use the wrong member: a failing header as `source.pointer` `/{header}`
  (`Endpoints/RequestRefusal.cs`), `atomic` as `source.pointer` `/atomic`
  (`Endpoints/BulkChecks.cs`), and a missing required filter at `/data`.
- A member renamed with `[JsonPropertyName]` is reported by its C# name in validation errors.
- Resource documents are validated a level at a time, so not every error is reported at once
  (`ResourceDocumentBinding.cs`).
- A status-only `500` has no error `id`, though its detail asks the caller to quote one
  (`StatusCodeErrorWriter.cs`).
- `InvalidRequestException` always has the title "A field has an invalid value." whatever its code
  (`ErrorResponseExceptionHandler.cs`).
- An enumeration value in another case on a resource attribute is refused `invalid-value`: binding
  checks the name ignoring case (`ResourceDocumentBinding.cs`), then Service's case-sensitive
  `JsonStringEnumConverter` can't read it (`ResourceDocumentReading.cs`).
- Enumerations elsewhere: plain JSON bodies accept only the exact upper snake case name and bind
  undefined integers; route, query and header parameters accept C# member names and any number and
  refuse the upper snake case name (`Endpoints/RequestParameters.cs`). Only `Filter<T>` uses
  `EnumValues`.
- `in` ignores its operands' order: a string literal on either side means "the field contains the
  literal", the reverse of JSON Logic when the field comes first (`Querying/QueryableQuery.cs`,
  `Contains`).
- An `and` with one term is refused as "'And' combines two or more terms", the C# name rather than
  the JSON spelling (`Querying/QueryableQuery.cs`).

**Features the standards describe that the package doesn't provide**

- Deprecation: no `deprecated` flag and no `Deprecation`, `Sunset` or `Link` headers.
- Partial updates: no `PATCH` attribute, and `ResourceDocument<T>` can't tell an explicit `null`
  from an absent member.
- Asynchronous work: no `202`, job resource, `Prefer: respond-async` or `Preference-Applied`.
- Content negotiation: `Accept` is ignored and `406` never produced.
- Generated documentation: no example JSON, a search's `[Filterable]` fields aren't listed, and a
  bulk operation's execution mode isn't documented.

**Startup checks that miss a mistake**

- Operations without a standard attribute get no operation identifier, and the service still starts
  (`Endpoints/StandardEndpointConvention.cs`; `Endpoints/StandardEndpointCheck.cs` checks only that
  names are unique).
- An attribute with an empty template, the default for `[StandardCreate]` and `[StandardReadMany]`,
  matches any route (`StandardEndpointCheck.Matches`).
- Nothing enforces `api/v{n}/`, lowercase or kebab case on a service's own routes.
- Sortable declaration mistakes (a missing or repeated default, a field declared twice, a bad
  `MappedTo`) throw on first use, not at startup, and `SortedBy` over resources throws when a
  `MappedTo` names another member.

**Other surprises**

- A `PUT` that sets `201` gets the request path plus `/{id}` as `Location`
  (`Endpoints/StandardEndpointFilter.cs`), and the OpenAPI document shows only the `200`.
- `[SelfHostedOnly]` and `[NotSelfHostedOnly]` apply only to endpoints mapped with
  `WithStandards()`, after binding and validation, and refuse with `invalid-value` as if a field
  were at fault (`Endpoints/StandardEndpointFilter.cs`).
- Documents, the error document included, are written with the host's `JsonOptions`
  (`StatusCodeErrorWriter.cs`, `ErrorResponseExceptionHandler.cs`, `ResourceDocument.cs`), so a
  host without Service's settings writes `null` members such as `"id": null`.

## Testing

Every source file has a matching test class in `tests/Bitwarden.Server.Sdk.Resources.Tests`, in a
folder that mirrors `src`. They test the package's types on their own.

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Resources/tests/Bitwarden.Server.Sdk.Resources.Tests
```

Requests through the whole pipeline and the OpenAPI document built end to end are tested in
Service's projects, because a resource is always served by a service. Run both when a change alters
what a caller sees:

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Service/tests/Bitwarden.Server.Sdk.Service.Tests
dotnet test --project extensions/Bitwarden.Server.Sdk.Service/tests/Bitwarden.Server.Sdk.Service.MinimalApi.Tests
```

- `Bitwarden.Server.Sdk.Service.Tests` compiles its handlers with the request delegate generator, as
  a service does; `Bitwarden.Server.Sdk.Service.MinimalApi.Tests` compiles the same files without
  it. A change must pass both.
- `MinimalApiParity.golden.txt` records the status, headers and body of every request in
  `MinimalApiParityTests`' matrix, and `MinimalApiParity.openapi.golden.json` the published
  document. A change that alters either changes what every caller sees; when it is deliberate, rerun
  with `RECORD_PARITY=1` and review the diff.

## Documentation

The developer guide for this package is in Service's [`docs/`](../../Bitwarden.Server.Sdk.Service/docs/README.md),
and [Service's README](../../Bitwarden.Server.Sdk.Service/src/README.md) says how it is maintained.
