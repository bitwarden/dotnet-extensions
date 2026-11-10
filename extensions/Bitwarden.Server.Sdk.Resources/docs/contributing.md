# Contributing to Bitwarden.Server.Sdk.Resources

This page is for working on the package itself. To write a service's API with it, start at the
[README](../README.md) and the [developer guide](README.md).

## Running the tests

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Resources/tests/Bitwarden.Server.Sdk.Resources.Tests
```

These test the package's types on their own: binding, validation, querying, the standard
attributes, the endpoint checks and the OpenAPI transformers.

Tests that need a running service, with requests going through the whole pipeline and the OpenAPI
document built end to end, are in Service's test projects, because a resource is always served by a
service:

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Service/tests/Bitwarden.Server.Sdk.Service.Tests
dotnet test --project extensions/Bitwarden.Server.Sdk.Service/tests/Bitwarden.Server.Sdk.Service.MinimalApi.Tests
```

Run both when a change alters what a caller sees.

## Changing this package

- **What belongs here.** What a service's API looks like to its callers: resource documents, the
  standard operations and their checks, the error document, querying, bulk operations, and how they
  are described in the OpenAPI document. Hosting, authentication, telemetry and the general OpenAPI
  setup belong in Bitwarden.Server.Sdk.Service; the error types a service and its callers both need
  belong in Bitwarden.Server.Sdk.Errors.
- **Entry points stay few and explicit.** A host turns the package on with `AddStandardErrors`,
  `AddStandardEndpoints`, `UseStandardStatusCodePages`, `WithStandards`, `RequireStandards` and
  `DescribeResources`. Service calls them for every service. A new behaviour hangs off one of these
  rather than asking each service for another call.
- **Handlers behave the same with and without the request delegate generator.** Service's main test
  project compiles its handlers with the generator, as a service does. Its MinimalApi project
  compiles the same test files without it, so the framework's runtime request delegate factory is
  covered too. A change must pass both.
- **The golden files pin what a caller sees.** `MinimalApiParity.golden.txt` in Service's tests
  records the status, headers and body of every request in `MinimalApiParityTests`' matrix, and
  `MinimalApiParity.openapi.golden.json` the published OpenAPI document. A change here that alters
  either is a change every caller sees. When it is deliberate, rerun with `RECORD_PARITY=1` and
  review the diff.
- **Startup checks fail loudly.** A declaration the package can check when endpoints are built is
  reported through `RequireStandards()`, every problem at once, rather than on the first request.
- **Zero knowledge.** Services store and pass on Vault Data only as Protected Data. `[Encrypted]`
  checks an encrypted value's length and shape and nothing else. Nothing here decrypts, logs or
  inspects an encrypted value, and no test fixture may hold
  plaintext Vault Data.

## Changing the documentation

- **`README.md`**, at the package root, introduces the package to someone writing a service's API:
  what it is, how to start and a few short examples.
- **`docs/`** is the developer guide: how to write code with the package, one topic per page. Each
  page is code a service writes and what the developer needs to know about it. It doesn't restate
  or link the API standards, and it shows no example requests or responses.
- **`src/PACKAGE.md`** is the readme shown on nuget.org. It stays short and links to the guide with
  absolute URLs, because nuget.org doesn't resolve relative links.
- **Samples must compile.** A sample shows code as a service writes it. Before changing one, build
  it against the package in a scratch web project with `EnableRequestDelegateGenerator` on.
- A behaviour change that a page describes updates that page in the same pull request.
- **`OPEN-ISSUES.md`** lists the package's known gaps. A fix removes its entry and updates the guide
  page that described the workaround.
