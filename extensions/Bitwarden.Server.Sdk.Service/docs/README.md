# Bitwarden.Server.Sdk.Service developer guide

How to build a Bitwarden service with `Bitwarden.Server.Sdk.Service`, one topic per page. New to the
package? Start with [Getting started](../src/README.md#getting-started) in the README.

Bitwarden's [service-to-service API standards](https://contributing.bitwarden.com/architecture/server/apis)
say how a service's API looks and behaves. This guide follows their structure. Each page takes one
standard and shows:

- **What the package does** for it, so you don't write it yourself.
- **Code**, compiled against the package, showing how a service uses it.
- **What you decide**: what the standard leaves to the service, or what the package doesn't do.

The examples share one small service: a `widget-service` whose people keep widgets. A `Widget` has a
`Name` and `Notes` the person encrypts on their device. Types named `Widget...`, such as
`IWidgetStore`, are the service's own.

## Contracts

How an API is described, addressed, versioned and retired.

- [OpenAPI](contracts/openapi.md): the generated document, the documentation UI, operation
  identifiers and committing the contract.
- [API paths](contracts/api-paths.md): collection and resource paths, which identifiers belong in
  the path, and working across several scopes.
- [Breaking changes and versioning](contracts/breaking-changes-and-versioning.md)
- [Unrecognized fields, query parameters, and headers](contracts/unrecognized-fields-query-parameters-and-headers.md)
- [Deprecation](contracts/deprecation.md)

## Payloads and responses

What goes over the wire, and what a caller gets back.

- [Payload shape](payloads-and-responses/payload-shape.md): resources, the document envelope and resource fields.
- [JSON](payloads-and-responses/json.md): dates, enumerations and free-form values.
- [Naming conventions](payloads-and-responses/naming-conventions.md)
- [Standard fields](payloads-and-responses/standard-fields.md): `id`, `type`, `version` and the audit
  fields.
- [Content negotiation](payloads-and-responses/content-negotiation.md)
- [Standard responses](payloads-and-responses/standard-responses.md): the response helpers and
  status codes.
- [Request validation](payloads-and-responses/request-validation.md)
- [Errors](payloads-and-responses/errors.md): the error document and the exceptions that produce it.

## Operations

Creating, reading, changing and deleting resources, and acting upon them.

- [Verbs](operations/verbs.md): the standard attributes and how routes are checked.
- [Creating resources](operations/creating-resources.md)
- [Reading resources](operations/reading-resources.md)
- [Updating resources](operations/updating-resources.md)
- [Partial updates](operations/partial-updates.md)
- [Optimistic concurrency](operations/optimistic-concurrency.md)
- [Deleting resources](operations/deleting-resources.md), including soft deletes.
- [Acting upon resources](operations/acting-upon-resources.md)

## Querying

Reading a collection the way the caller asks.

- [Filtering](querying/filtering.md), including multi-value parameters and ranges.
- [Paging](querying/paging.md): offset and cursor paging.
- [Sorting](querying/sorting.md)
- [Sparse fieldsets](querying/sparse-fieldsets.md)
- [Advanced queries](querying/advanced-queries.md): searches with filter expressions.

## Bulk and asynchronous work

Working on many resources in one request.

- [Bulk operations](bulk-and-asynchronous-work/bulk-operations/index.md)
    - [Targeting](bulk-and-asynchronous-work/bulk-operations/targeting.md)
    - [Per-resource rules](bulk-and-asynchronous-work/bulk-operations/per-resource-rules.md)
    - [All-or-nothing and best-effort](bulk-and-asynchronous-work/bulk-operations/all-or-nothing-and-best-effort.md)
    - [Synchronous and asynchronous](bulk-and-asynchronous-work/bulk-operations/synchronous-and-asynchronous.md)
    - [Bulk responses](bulk-and-asynchronous-work/bulk-operations/bulk-responses.md)
    - [Bulk creates](bulk-and-asynchronous-work/bulk-operations/bulk-creates.md)
    - [Bulk updates](bulk-and-asynchronous-work/bulk-operations/bulk-updates.md)
    - [Bulk replaces](bulk-and-asynchronous-work/bulk-operations/bulk-replaces.md)
    - [Bulk deletes](bulk-and-asynchronous-work/bulk-operations/bulk-deletes.md)
    - [Bulk actions](bulk-and-asynchronous-work/bulk-operations/bulk-actions.md)
- [Jobs](bulk-and-asynchronous-work/jobs.md)

## Beyond the API standards

What every service needs that the API standards don't cover.

- [Defining a service](beyond-the-standards/defining-a-service.md): the `Service` class, its hooks,
  the middleware order and the three kinds of service.
- [Authentication and authorization](beyond-the-standards/authentication-and-authorization.md):
  scopes, organization, user and provider requirements, roles, and token audiences.
- [Caller context](beyond-the-standards/caller-context.md): whom a call between services concerns,
  and how a service knows to believe it.
- [Configuration and self-hosting](beyond-the-standards/configuration-and-self-hosting.md):
  settings, self-hosted installs, feature flags and the config resource.
- [Network and request limits](beyond-the-standards/network-and-request-limits.md): trusted proxies,
  per-address rate limits and browser origins.
- [Outbound HTTP](beyond-the-standards/outbound-http.md): calls guarded against SSRF, and internal
  TLS.
- [Observability and health](beyond-the-standards/observability-and-health.md): traces, metrics,
  logs and health probes.
- [Caching and extensions](beyond-the-standards/caching-and-extensions.md): a short in-process memo,
  and extending the platform from a package.

## Working on the package

See [Contributing](contributing.md).
