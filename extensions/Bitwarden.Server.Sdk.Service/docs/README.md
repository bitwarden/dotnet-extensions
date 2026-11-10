# Bitwarden.Server.Sdk.Service developer guide

How to run a Bitwarden service with `Bitwarden.Server.Sdk.Service`, one topic per page: defining
the service, securing it, configuring it and watching it run. New to the package? Start with
[Getting started](../README.md#getting-started) in the README.

Each page shows the code to write, compiled against the package, and what you need to know about
it: what the package does for you, what stops the service starting, and what is left to you.

The examples share one small service: a `widget-service` whose people keep widgets. A `Widget` has a
`Name` and `Notes` the person encrypts on their device. Types named `Widget...`, such as
`IWidgetStore`, are the service's own.

## Running a service

Defining the service itself, securing it, configuring it and running it.

- [Defining a service](running-a-service/defining-a-service.md): the `Service` class, its hooks,
  the middleware order and the three kinds of service.
- [Authentication and authorization](running-a-service/authentication-and-authorization.md):
  scopes, organization, user and provider requirements, roles, and token audiences.
- [Context](running-a-service/context.md): whom a call between services concerns,
  and how a service knows to believe it.
- [Configuration and self-hosting](running-a-service/configuration-and-self-hosting.md):
  settings, self-hosted installs, feature flags and the config resource.
- [Network and request limits](running-a-service/network-and-request-limits.md): trusted proxies,
  per-address rate limits and browser origins.
- [Outbound HTTP](running-a-service/outbound-http.md): calls guarded against SSRF, and internal
  TLS.
- [Observability and health](running-a-service/observability-and-health.md): traces, metrics,
  logs and health probes.
- [Caching and extensions](running-a-service/caching-and-extensions.md): a short in-process memo,
  and extending the platform from a package.

## Resources and their operations

Writing a service's resources, and the operations on them, is in Bitwarden.Server.Sdk.Resources'
[developer guide](../../Bitwarden.Server.Sdk.Resources/docs/README.md), in five groups:

- [Contracts](../../Bitwarden.Server.Sdk.Resources/docs/README.md#contracts): OpenAPI, API paths, versioning, unrecognized input and deprecation.
- [Payloads and responses](../../Bitwarden.Server.Sdk.Resources/docs/README.md#payloads-and-responses): resources, JSON, standard fields,
  responses, validation and errors.
- [Operations](../../Bitwarden.Server.Sdk.Resources/docs/README.md#operations): creating, reading, updating, partial updates, optimistic
  concurrency, deleting and actions.
- [Querying](../../Bitwarden.Server.Sdk.Resources/docs/README.md#querying): filtering, paging, sorting, sparse fieldsets and searches.
- [Bulk and asynchronous work](../../Bitwarden.Server.Sdk.Resources/docs/README.md#bulk-and-asynchronous-work): bulk operations and jobs.

## Working on the package

See [Contributing](contributing.md). [Open issues](../OPEN-ISSUES.md) lists the package's known gaps;
each page here describes what it does today.
