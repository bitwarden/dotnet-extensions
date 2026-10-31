# Bitwarden.Server.Sdk.Common

## About

The small set of types that a Bitwarden service and the code that calls it both need to agree on:
what an error response looks like, which error codes a caller can rely on, which headers carry
caller context, and which exception means "not found", "conflict" or "bad request".

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

### Find out who is calling

`IContext` tells you which organization a request is about and which user it is on behalf of. In a
service, inject it:

```csharp
public class WidgetHandler(IContext context)
{
    public Task<IReadOnlyList<Widget>> ListAsync() =>
        _store.ListForOrganizationAsync(context.OrganizationId!.Value);
}
```

The organization and user tell you what a request concerns. They are never permission to do it: your
service still checks its own access rules.

### Do work that didn't come from a request

A background job or a message from a queue has no incoming request to carry the caller's context.
State it yourself for the duration of the work:

```csharp
using (ContextScope.Begin(new StatedContext { OrganizationId = message.OrganizationId }))
{
    await _billingClient.RecalculateAsync(message.OrganizationId);
}
```

Don't do this while handling a request. There, the context comes from the request itself.

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

Only the length is checked. The value is never read or decrypted.

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

The digest keeps the address out of the cache, but anyone who can read the cache and guess an
address can confirm it, so treat the cache as holding personal data.

## Types

| Type                                                                     | Purpose                                                                                        |
| ------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------- |
| `ErrorResponse`, `Error`, `ErrorSource`, `FailedResource`                | The body of every unsuccessful response (namespace `Bitwarden.Server.Sdk.Common.Errors`)       |
| `ErrorCodes`, `ErrorTitles`, `ErrorDetails`                              | The codes a caller branches on, and the text that accompanies them (also in `.Errors`)         |
| `NotFoundException`, `ConflictException`, `InvalidRequestException`, ... | The standard failures, each answered with its own status                                       |
| `IProvideErrors`                                                         | An exception that reports several errors at once                                               |
| `IExceptionTranslator`                                                   | Says which standard exception a library's own exception means                                  |
| `BitwardenHeaders`                                                       | The headers that carry caller context between services                                         |
| `CallerContextHeader`, `CallerContextValue`, `CallerCredential`          | Writes and strictly reads the `Bitwarden-Context` header                                       |
| `CallerContextToken`, `CallerContextAssertion`, `ICallerProof`           | The signed form of a call's context, what one call asserts, and the token it is exchanged from |
| `IContext`, `ContextScope`, `StatedContext`                              | Who and what the current operation concerns                                                    |
| `IRowOwnership`, `DefaultRowOwnership`                                   | The owner a restricted query is narrowed to, refusing when there is none                       |
| `RequestMemo`                                                            | Facts read once in a request and remembered until it ends                                      |
| `IRequestNamedOwner`, `IOrganizationCreation`                            | Owners an operation names itself, for its outgoing calls                                       |
| `IServiceIdentity`, `TokenAudience`                                      | Which service this is, and which token audience it accepts                                     |
| `EncryptedAttribute`, `PersonalDataAttribute`                            | Mark a field as Protected Data or as personal data                                             |
| `OwnedCacheEntry`                                                        | How a service-owned cache entry is stored in Redis                                             |
| `OwnedAnswerKey`                                                         | How a service-owned answer is keyed in the same cache                                          |
| `InternalTls`                                                            | Which certificate authorities a service trusts for calls between services                      |
| `IServiceExtension`, `ServiceExtensionAttribute`                         | How a package configures every service that references it                                      |
