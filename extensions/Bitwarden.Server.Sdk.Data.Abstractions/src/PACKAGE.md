# Bitwarden.Server.Sdk.Data.Abstractions

## About

Declarations a Bitwarden service's model and its data layer share: `[Encrypted]` for a field holding Protected Data
(ciphertext the service never decrypts), `[PersonalData]` for an attribute about an identifiable person,
`IRowOwnership` for the owner every restricted query is narrowed to, and `RequestMemo` for facts read once per request.

It references only Bitwarden.Server.Sdk.Context, Bitwarden.Server.Sdk.Errors and
Microsoft.Extensions.DependencyInjection.Abstractions, so a model can declare these without depending on a database or
on the packages that host a service.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Data.Abstractions
```

## Configuration

The package reads no settings.

## Getting started

Mark a field that holds ciphertext, with the longest value it accepts:

```csharp
using Bitwarden.Server.Sdk.Data;

public sealed record Note
{
    [PersonalData]
    public required string Title { get; init; }

    // Encrypted on the person's device. Only its length and shape are checked.
    [Encrypted(MaxLength = 10_000, WellFormed = true)]
    public required string Body { get; init; }
}
```

A value longer than `MaxLength` is refused with `The field Body must be an encrypted string of at most 10000
characters.` With `WellFormed = true`, a value that isn't shaped like an encrypted string is refused with `Body is not a
valid encrypted string.` Nothing is decrypted. See the
[README](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Data.Abstractions/src/README.md)
for a complete program.

## Documentation

### Narrow a query to its owner

```csharp
using Bitwarden.Server.Sdk.Data;

public sealed class SharedWidgetQueries(IRowOwnership ownership, IWidgetRows rows)
{
    public Task<IReadOnlyList<Widget>> ListAsync(CancellationToken cancellationToken) =>
        rows.ListAsync(ownership.RequireOrganization(), cancellationToken);

    // Rows a person or an organization may own: an absent organization is ordinary here.
    public Task<IReadOnlyList<Widget>> ListMineAsync(CancellationToken cancellationToken) =>
        ownership.Organization() is { } organization
            ? rows.ListAsync(organization, cancellationToken)
            : rows.ListForUserAsync(ownership.RequireUser(), cancellationToken);
}
```

`RequireOrganization()`, `RequireUser()` and `RequireProvider()` throw `ForbiddenException` when the request names no
such owner, so a missing header is refused rather than read as "no restriction". `DefaultRowOwnership` reads the owner
from `IContext`; Bitwarden.Server.Sdk.Service registers it for every service.

### Remember a lookup for the rest of a request

```csharp
using Bitwarden.Server.Sdk.Data;
using Microsoft.Extensions.DependencyInjection;

public static class WidgetGrantsRegistration
{
    public static IServiceCollection AddWidgetGrants(this IServiceCollection services) =>
        services.AddRequestMemo().AddScoped<WidgetGrants>();
}

public sealed class WidgetGrants(RequestMemo memo, IWidgetRows rows)
{
    public Task<IReadOnlyList<Guid>> CollectionsAsync(Guid organization, Guid member) =>
        memo.RememberAsync("collections", organization, member, () => rows.ReadCollectionsAsync(organization, member));
}
```

A memo lives for one request and is never shared across callers. Call `Forget()` after a write that could change a
remembered fact.

### Types

| Type                                   | Purpose                                                                  |
| -------------------------------------- | ------------------------------------------------------------------------ |
| `EncryptedAttribute`                   | Marks a string as Protected Data, with an explicit length bound          |
| `EncryptedStringFormat`                | Whether a string is shaped like an encrypted string, without decoding it |
| `PersonalDataAttribute`                | Marks an attribute as personal data about an identifiable person         |
| `IRowOwnership`, `DefaultRowOwnership` | The owner a restricted query is narrowed to, refusing when there is none |
| `RequestMemo`, `AddRequestMemo()`      | Facts read once in a request and remembered until it ends                |
