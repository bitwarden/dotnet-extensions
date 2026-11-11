# Open issues

Places where the package does not yet meet the
[API standards](https://contributing.bitwarden.com/architecture/server/apis), or behaves in a way a
service author would not expect. They were found while writing the [developer guide](docs/README.md),
which describes what the package does today and how a service works around each one.

Each entry names the standard's rule, what the package does instead, and where in the source. A fix
removes its entry and updates the guide page that describes the workaround.

## Startup checks that miss a mistake

- **A role requirement on a domain service isn't refused at startup,** but `ICallerRole` always
  answers `None` there, so every call is refused `403` (`Bitwarden.Server.Sdk.Authorization/src/OperationAuthorization.cs`,
  `Bitwarden.Server.Sdk.Authorization/src/ContextRequirementsHandler.cs`). Guide:
  [Authentication and authorization](docs/running-a-service/authentication-and-authorization.md).

## Other surprises

- **`GetOrResolveOnceAsync` needs `IMemoryCache`, which the platform doesn't register.** A service
  calls `AddMemoryCache()` itself. Guide: [Caching and extensions](docs/running-a-service/caching-and-extensions.md).
