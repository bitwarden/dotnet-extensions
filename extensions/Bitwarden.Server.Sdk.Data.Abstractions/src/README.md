# Bitwarden.Server.Sdk.Data.Abstractions

Declare what a field holds, and whose rows a query may reach, where a service's model and its data layer both see it.

A Bitwarden service stores Vault Data only as Protected Data: ciphertext a client encrypted, which the service never
decrypts. Its model says which fields hold that ciphertext and which hold personal data, and its queries are narrowed
to the organization, user or provider the request concerns. This package holds those declarations, so a model, a
data layer and the packages that read them agree without depending on each other.

- **Protected Data, bounded.** `[Encrypted(MaxLength = ...)]` marks a string as ciphertext and replaces a default
  length limit with an explicit one. `WellFormed = true` also checks its shape, without decoding or decrypting it.
- **Personal data, marked.** `[PersonalData]` marks an attribute about an identifiable person, wherever a copy could
  reach more readers than the resource does.
- **Rows narrowed to their owner.** `IRowOwnership` supplies the owner a query is narrowed to, and refuses when the
  request names none, rather than reading every owner's rows.
- **Facts read once per request.** `RequestMemo` remembers an answer for the rest of one request.

## What belongs here

Only what both a service's model and the code that stores, caches or validates it need. Nothing here touches
`HttpContext` or a database: Bitwarden.Server.Sdk.Resources reads `[Encrypted]` to validate and describe requests, a
cache shared across owners reads both markers to keep those fields out of it, and a data layer narrows its queries
through `IRowOwnership`. Bitwarden.Server.Sdk.Service registers `DefaultRowOwnership` for every service.

It references Bitwarden.Server.Sdk.Context (`DefaultRowOwnership` reads `IContext`), Bitwarden.Server.Sdk.Errors
(it refuses with `ForbiddenException`) and Microsoft.Extensions.DependencyInjection.Abstractions (for
`AddRequestMemo()`), and nothing else.

## Getting started

```bash
dotnet new console -n WidgetModel
cd WidgetModel
dotnet add package Bitwarden.Server.Sdk.Data.Abstractions --prerelease
```

```csharp
using System.ComponentModel.DataAnnotations;
using Bitwarden.Server.Sdk.Data;

var widgets = new[]
{
    new Widget { Name = "Kitchen", Notes = "2.AAAAAAAAAAAAAAAAAAAAAA==|c2VjcmV0|AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=" },
    new Widget { Name = "Garage", Notes = "my plaintext notes" },
};

foreach (var widget in widgets)
{
    var results = new List<ValidationResult>();
    var valid = Validator.TryValidateObject(widget, new ValidationContext(widget), results, validateAllProperties: true);

    Console.WriteLine(valid ? $"{widget.Name}: valid" : $"{widget.Name}: {string.Join(" ", results.Select(result => result.ErrorMessage))}");
}

public sealed record Widget
{
    [PersonalData]
    public required string Name { get; init; }

    // Encrypted on the person's device. The service checks its length and shape, and never decrypts it.
    [Encrypted(MaxLength = 10_000, WellFormed = true)]
    public string? Notes { get; init; }
}
```

`dotnet run` prints `Kitchen: valid` and `Garage: Notes is not a valid encrypted string.`

## Examples

### Narrow a query to its owner

Ask `IRowOwnership` for the owner, and filter by it. `RequireOrganization()` throws `ForbiddenException` (`403`) when
the request names no organization, so a missing header never becomes a read of every organization's rows:

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

`DefaultRowOwnership` reads the owner from `IContext`. A service whose rows are owned by something else registers its
own `IRowOwnership`.

### Remember a lookup for the rest of a request

Register the memo once, then ask it for a fact. It is read the first time and remembered until the request ends:

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

- A memo is scoped to one request, and each fact is keyed by its name, the organization and the subject, so it is
  never shared across callers.
- Call `Forget()` after a write that could change a remembered fact.
- A memo is not thread-safe: a request's work runs one step at a time.

## Configuration

The package reads no settings.

## Security

- **Nothing is decrypted.** `[Encrypted]` checks a length and, with `WellFormed`, the encryption type, the pieces that
  type calls for, and their base64. It can't tell real ciphertext from random bytes of the right shape: it keeps out
  plaintext a client sent by mistake, not a client that means to send something else.
- **Every `[Encrypted]` needs a bound.** A `MaxLength` that isn't positive throws when the attribute validates, and
  Bitwarden.Server.Sdk.Resources refuses it on a resource.
- **Refuse rather than widen.** Use the `Require` methods for rows that always have an owner of that kind. The nullable
  ones are for rows a person or an organization may own.

## Requirements

- .NET 10.
- Bitwarden.Server.Sdk.Context, Bitwarden.Server.Sdk.Errors and
  Microsoft.Extensions.DependencyInjection.Abstractions.
- An `IContext` registration for `DefaultRowOwnership`: Bitwarden.Server.Sdk.Authorization provides one.

## Contributing

Every type with logic has a test class of the same name in the package's `tests` folder. Run them from the repository root:

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Data.Abstractions/tests/Bitwarden.Server.Sdk.Data.Abstractions.Tests
```

A change must keep:

- **No decryption.** Nothing in the package decodes or decrypts an encrypted value.
- **Refusal over widening.** An owner that is absent is refused, never read as "no restriction".
- **Few dependencies, and nothing about the transport or the database.** No `HttpContext`, no Entity Framework.
