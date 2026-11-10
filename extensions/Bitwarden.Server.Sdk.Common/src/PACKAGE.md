# Bitwarden.Server.Sdk.Common

## About

The small set of types that a Bitwarden service and the code that calls it both need to agree on:
what an error response looks like, which error codes a caller can rely on, and which exception
means "not found", "conflict" or "bad request". The caller-context headers, values and tokens are in
Bitwarden.Server.Sdk.CallerContext.

The package has almost no dependencies, so a program that only calls a service does not have to
depend on the framework that hosts it.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Common
```

## Usage

### Report a failure the standard way

Throw one of the standard exceptions. The service host turns each into the right HTTP status and a
standard error document.

```csharp
using Bitwarden.Server.Sdk.Common;

public async Task<Widget> GetWidgetAsync(Guid id)
{
    var widget = await _store.FindAsync(id)
        ?? throw new NotFoundException("widget", id.ToString());

    if (widget.IsLocked)
    {
        throw new ConflictException("The widget is locked.");
    }

    return widget;
}

public void Rename(Widget widget, string name)
{
    if (string.IsNullOrWhiteSpace(name))
    {
        throw new InvalidRequestException("A name is required.", pointer: "/data/attributes/name");
    }
}
```

| Exception                     | Status |
| ----------------------------- | ------ |
| `InvalidRequestException`     | 422    |
| `UnauthenticatedException`    | 401    |
| `ForbiddenException`          | 403    |
| `NotFoundException`           | 404    |
| `ConflictException`           | 409    |
| `PendingServiceException`     | 501    |
| `ServiceUnavailableException` | 503    |

A caller branches on the stable `code` in the error document, never on the human-readable text. The
error document's types and codes are in `Bitwarden.Server.Sdk.Common.Errors`:

```csharp
using Bitwarden.Server.Sdk.Common.Errors;

if (error.Code == ErrorCodes.NotFound)
{
    // ...
}
```

### Remember a lookup for the rest of a request

If several parts of one request need the same fact, look it up once:

```csharp
services.AddRequestMemo();

var grants = await memo.RememberAsync("grants", organizationId, userId, () => LoadGrantsAsync(userId));
```

The remembered value is thrown away when the request ends, and is never shared between callers.

### Mark a field that holds encrypted data

Bitwarden servers only store encrypted data and never decrypt it. An encrypted string is longer
than a typical default string limit, so declare the field and its maximum length:

```csharp
[Encrypted(MaxLength = 10_000)]
public required string Notes { get; init; }
```

Only the length is checked, unless you ask for the shape too. `WellFormed = true` also refuses a value
that isn't shaped like an encrypted string (its type, pieces and base64), as "Notes is not a valid
encrypted string.":

```csharp
[Encrypted(MaxLength = 10_000, WellFormed = true)]
public required string Notes { get; init; }
```

A value that is too long as well is refused as malformed, and so is a value that isn't a string. The
value is never decrypted. `EncryptedStringFormat.IsWellFormed` makes the same check in code.

### Trust an internal certificate authority

If your services' certificates come from an internal authority, name its root certificate under
`Tls:CertificateAuthorities` and check each server's certificate with `InternalTls.Trusts`:

```csharp
var tls = configuration.GetSection(InternalTls.Section).Get<InternalTls>() ?? new InternalTls();
var authorities = tls.LoadAuthorities();

var handler = new SocketsHttpHandler
{
    SslOptions =
    {
        RemoteCertificateValidationCallback = (_, certificate, chain, errors) =>
            InternalTls.Trusts(authorities, certificate, chain, errors),
    },
};
```

A certificate is trusted if the system trusts it, or if it chains to a named authority, is valid
now and is allowed for server authentication. A name that doesn't match is never trusted.

### Key a cached answer by text

When a cached answer is keyed by text such as an email address, store only its digest. The service
and its clients reach the same subject from the same normalized text:

```csharp
var subject = OwnedAnswerKey.Of("account-by-email", email.Trim().ToLowerInvariant());
var key = OwnedCacheEntry.Key(OwnedAnswerKey.Template("identity-service", "account-by-email"), subject);
```

With no secret, the digest is plain SHA-256: it keeps the address out of the cache, but anyone who
can read the cache and guess an address can confirm it. So a shared secret makes the digest
HMAC-SHA-256, and outside `Development` a host with a service cache must have one:

```csharp
var secret = OwnedAnswerKey.ReadSecret(configuration, environment.EnvironmentName);
var subject = OwnedAnswerKey.Of("account-by-email", email.Trim().ToLowerInvariant(), secret);
```

`ReadSecret` reads `Caching:Owned:AnswerSecret` (base64, at least 32 bytes). It throws when the
secret is unset, `Caching:Owned:Redis:Configuration` is set and the environment is not
`Development`; otherwise an unset secret is empty and the digest is plain. Call it as the host
starts. Every service and client sharing the cache must use the same secret.

## Types

| Type                                                                     | Purpose                                                                                  |
| ------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------- |
| `ErrorResponse`, `Error`, `ErrorSource`, `FailedResource`                | The body of every unsuccessful response (namespace `Bitwarden.Server.Sdk.Common.Errors`) |
| `ErrorCodes`, `ErrorTitles`, `ErrorDetails`                              | The codes a caller branches on, and the text that accompanies them (also in `.Errors`)   |
| `NotFoundException`, `ConflictException`, `InvalidRequestException`, ... | The standard failures, each answered with its own status                                 |
| `IProvideErrors`                                                         | An exception that reports several errors at once                                         |
| `IExceptionTranslator`                                                   | Says which standard exception a library's own exception means                            |
| `RequestMemo`                                                            | Facts read once in a request and remembered until it ends                                |
| `EncryptedAttribute`, `PersonalDataAttribute`                            | Mark a field as Protected Data or as personal data                                       |
| `OwnedCacheEntry`                                                        | How a service-owned cache entry is stored in Redis                                       |
| `OwnedAnswerKey`                                                         | How a service-owned answer is keyed in the same cache                                    |
| `InternalTls`                                                            | Which certificate authorities a service trusts for calls between services                |
| `IServiceExtension`, `ServiceExtensionAttribute`                         | How a package configures every service that references it                                |
