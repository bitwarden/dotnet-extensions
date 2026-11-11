# Bitwarden.Server.Sdk.Data.Abstractions

## About

Declarations a Bitwarden service's model and its data layer share. A service stores Vault Data only as Protected Data:
ciphertext a client encrypted, which the service never decrypts. This package lets a model say which fields hold that
ciphertext (`[Encrypted]`) and which hold personal data (`[PersonalData]`), lets a data layer narrow every restricted
query to the owner the request concerns (`IRowOwnership`), and remembers facts read once per request (`RequestMemo`).

It references only Bitwarden.Server.Sdk.Context, Bitwarden.Server.Sdk.Errors and
Microsoft.Extensions.DependencyInjection.Abstractions, so a model can declare these without depending on a database or
on the packages that host a service.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Data.Abstractions
```

## Configuration

The package reads no settings.

## Usage

### Mark a field that holds encrypted data

An encrypted string carries its encryption type, an initialization vector, the ciphertext and a MAC, so even a short
secret is longer than a typical default string limit. `[Encrypted]` marks a string as ciphertext and replaces that limit
with the longest value you accept:

```csharp
using Bitwarden.Server.Sdk.Data;

public sealed record Note
{
    [PersonalData]
    public required string Title { get; init; }

    // Encrypted on the person's device. Only its length and shape are checked.
    [Encrypted(MaxLength = 10_000, WellFormed = true)]
    public string? Body { get; init; }
}
```

- A value longer than `MaxLength` is refused with `The field Body must be an encrypted string of at most 10000
characters.`
- With `WellFormed = true`, a value that isn't shaped like an encrypted string (a known encryption type, the pieces that
  type calls for, and their base64) is refused with `Body is not a valid encrypted string.` So is a value that isn't a
  string, and a malformed value that is also too long: the shape is reported before the length. `EncryptedStringFormat.IsWellFormed` makes the same check in code.
- A null value is valid; whether a member may be absent is left to its nullability.
- Every `[Encrypted]` needs a bound. A `MaxLength` that isn't positive throws `InvalidOperationException` when the
  attribute validates, and Bitwarden.Server.Sdk.Resources refuses it on a resource.
- Nothing is decrypted or decoded. The check can't tell real ciphertext from random bytes of the right shape: it keeps
  out plaintext a client sent by mistake, not a client that means to send something else.

`[PersonalData]` marks an attribute about an identifiable person that isn't Protected Data, such as a name or an
address. It changes nothing a caller sees or the service validates; code that could copy the attribute to more readers
than the resource has, such as a cache shared across owners, reads it to keep the field out.

### Narrow a query to its owner

Ask `IRowOwnership` for the owner, and filter by it:

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

`RequireOrganization()`, `RequireUser()` and `RequireProvider()` throw `ForbiddenException`, which a service answers
with `403`, when the request names no such owner. A missing header is refused rather than read as "no restriction".
Use them for rows that always have an owner of that kind, and the nullable `Organization()`, `User()` and `Provider()`
only for rows a person or an organization may own.

`DefaultRowOwnership` reads the owner from `IContext`, which Bitwarden.Server.Sdk.Authorization registers.
Bitwarden.Server.Sdk.Service registers `DefaultRowOwnership` as the `IRowOwnership` of every service. A service whose rows are owned by something else registers
its own `IRowOwnership`.

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
- A scope opened for the same request can use the request's facts by calling `Share(requestMemo)` on its own memo.
- A memo is not thread-safe: a request's work runs one step at a time.

## Types

| Type                                   | Purpose                                                                  |
| -------------------------------------- | ------------------------------------------------------------------------ |
| `EncryptedAttribute`                   | Marks a string as Protected Data, with an explicit length bound          |
| `EncryptedStringFormat`                | Whether a string is shaped like an encrypted string, without decoding it |
| `PersonalDataAttribute`                | Marks an attribute as personal data about an identifiable person         |
| `IRowOwnership`, `DefaultRowOwnership` | The owner a restricted query is narrowed to, refusing when there is none |
| `RequestMemo`, `AddRequestMemo()`      | Facts read once in a request and remembered until it ends                |
