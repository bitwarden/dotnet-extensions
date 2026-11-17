# Bitwarden.Server.Sdk.Data.Abstractions

**Audience:** engineers changing this package.

Data classification and row ownership shared by a Bitwarden service's model and its data layer. See
[PACKAGE.md](PACKAGE.md) for how to use it.

## What belongs here

Only what both a service's model and the code that stores, caches or validates it need, so a model, a data layer and
the packages that read the declarations agree without depending on each other. Bitwarden.Server.Sdk.Resources reads
`[Encrypted]` to validate and describe requests, a cache shared across owners reads both markers, and a data layer
narrows its queries through `IRowOwnership`.

Nothing that touches the transport or the database: no `HttpContext`, no Entity Framework. Keep the dependencies to
Context (`DefaultRowOwnership` reads `IContext`), Errors (it refuses with `ForbiddenException`) and
Microsoft.Extensions.DependencyInjection.Abstractions (for `AddRequestMemo()`).

## How it works

`EncryptedStringFormat` checks the shape `{type}.{piece}|{piece}...`: the type by number or name, then the pieces that
type calls for, each padded base64, with an AES IV decoding to 16 bytes and an HMAC-SHA256 MAC to 32. A string with no
type is one of the two legacy AES forms, told apart by its number of pieces. It computes decoded lengths from the text
and accepts padding bits that aren't zero, so nothing is decoded.

`EncryptedAttribute` overrides both `IsValid` overloads: the one taking a `ValidationContext` reports a malformed value
before a long one, with `FormatMalformedMessage`, so a caller learns the shape is wrong first. `EnsureBounded()` is
public so a package reading the attribute (Resources, when it builds a resource's validators and when it writes OpenAPI) refuses a missing bound
without validating a value.

A change must keep:

- **No decryption.** Nothing in the package decodes or decrypts an encrypted value.
- **Refusal over widening.** An owner that is absent is refused, never read as "no restriction".

## Testing

Every type with logic has a test class of the same name in
`tests/Bitwarden.Server.Sdk.Data.Abstractions.Tests`.

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Data.Abstractions/tests/Bitwarden.Server.Sdk.Data.Abstractions.Tests
```
