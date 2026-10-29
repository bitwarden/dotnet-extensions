# Bitwarden.Server.Sdk.Common

The small set of types that a Bitwarden service **and** the code that calls it both need to agree on.

## What problem does it solve?

When one program calls another over HTTP, both sides have to agree on a few things:

- What an error response looks like, and which error codes a caller can rely on.
- Which headers say "this request is about organization X, on behalf of user Y".
- Which exception means "not found", "conflict" or "bad request".

If those types lived in the package that *hosts* a service, every program that merely *calls* the
service would have to depend on the whole hosting framework. So they live here, in a package with
almost no dependencies, and both sides reference it.

You will rarely add this package yourself: `Bitwarden.Server.Sdk.Service` (for hosting a service) and
`Bitwarden.Server.Sdk.Client` (for calling one) both bring it in. You use its types directly.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Common
```

## Using it

### Report a failure the standard way

Throw one of the standard exceptions. A service built on `Bitwarden.Server.Sdk.Service` turns each
into the right HTTP status and a standard error document; you don't write any response code.

```csharp
using Bitwarden.Server.Sdk.Common;

public async Task<Widget> GetWidgetAsync(Guid id)
{
    var widget = await _store.FindAsync(id)
        ?? throw new NotFoundException("widget", id.ToString());         // 404

    if (widget.IsLocked)
    {
        throw new ConflictException("The widget is locked.");              // 409
    }

    return widget;
}

public void Rename(Widget widget, string name)
{
    if (string.IsNullOrWhiteSpace(name))
    {
        // 422, pointing at the field that was wrong.
        throw new InvalidRequestException("A name is required.", pointer: "/data/attributes/name");
    }
}
```

A caller branches on the stable `code` in the error document, never on the human-readable text.
The error document's types and codes are in `Bitwarden.Server.Sdk.Common.Errors`:

```csharp
using Bitwarden.Server.Sdk.Common.Errors;

if (error.Code == ErrorCodes.NotFound) { /* ... */ }
```

### Find out who is calling

`IContext` tells you which organization a request is about and which user it's on behalf of. In a
service, inject it:

```csharp
public class WidgetHandler(IContext context)
{
    public Task<IReadOnlyList<Widget>> ListAsync() =>
        _store.ListForOrganizationAsync(context.OrganizationId!.Value);
}
```

The organization and user tell you **what** a request concerns. They are never permission to do it:
your service still checks its own access rules.

### Do work that didn't come from a request

A background job or a message from a queue has no incoming request to carry the caller's context.
State it yourself for the duration of the work, and any service calls made inside will carry it:

```csharp
using (ContextScope.Begin(new StatedContext { OrganizationId = message.OrganizationId }))
{
    await _billingClient.RecalculateAsync(message.OrganizationId);
}
```

Don't do this while handling a request: there, the context comes from the request itself.

### Remember a lookup for the rest of a request

If several parts of one request need the same fact, look it up once:

```csharp
services.AddRequestMemo();

// later, inside a request:
var grants = await memo.RememberAsync("grants", organizationId, userId, () => LoadGrantsAsync(userId));
```

The remembered value is thrown away when the request ends, and is never shared between callers.

### Mark a field that holds encrypted data

Bitwarden servers only store encrypted data; they never decrypt it. A string field holding an encrypted
value is longer than the default 255-character limit, so declare it:

```csharp
[Encrypted(MaxLength = 10_000)]
public required string Notes { get; init; }
```

The service checks only the length. It never reads or decrypts the value.

## Learn more

[PACKAGE.md](PACKAGE.md) lists every type in the package, with the rules each one follows.

## Working on this package

### What belongs here

Only what **both** a service and a caller of that service need. The test is whether removing it
would force `Bitwarden.Server.Sdk.Client` to reference `Bitwarden.Server.Sdk.Service`.

Things that look shared but are not:

- `Resource`, `OneResource<T>`, `ManyResources<T>`: a service declares resources; a client
  consumes generated types for them. The client never sees these.
- `ResourceTypes`, `ReadOnlyAttribute`: the same. They are authoring concerns.
- Anything touching `HttpContext`, MVC, or `HttpClient`.

### Why it exists

`Bitwarden.Server.Sdk.Service` must be free to change without considering clients. That is only true
if clients do not reference it, which in turn is only true if the vocabulary they share lives
somewhere neither owns.
