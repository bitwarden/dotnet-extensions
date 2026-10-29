# Bitwarden.Server.Sdk.Common

The vocabulary a Bitwarden service and its callers both need.

Two sides of a call have to agree on some things: what an error document looks like, what the
stable error codes are, which headers carry caller context, and what exception a failure becomes.
If that vocabulary lived in the service package, every client would depend on the service
framework — and the framework could not then change without considering every consumer of every
service. It lives here so that neither side depends on the other.

| | |
| --- | --- |
| `ErrorResponse`, `Error`, `ErrorSource` | The body of every unsuccessful response |
| `ErrorCodes`, `ErrorTitles`, `ErrorDetails` | The codes a caller branches on, and the prose that accompanies them |
| `NotFoundException`, `ConflictException`, `InvalidRequestException` | Raised by a service, and by a client on the caller's behalf |
| `BitwardenHeaders` | The four headers carrying caller context from one service to another: `Bitwarden-Organization`, `Bitwarden-User`, `Bitwarden-Provider` and `Bitwarden-Context`; never a browser's to send |
| `CallerContextHeader`, `CallerContextValue`, `CallerCredential` | `Bitwarden-Context`: one closed, versioned JSON object (`v`, `clientIp`, `deviceType`, `deviceIdentifier`, `credential`, `recordedUser`, `recordedProvider`, `organizations`), written and read strictly, at most 16 KB |
| `IContext` | What the current request knows about its caller — organization, acting user, provider, the originating client's address and kind, and, audit-only, the kind of credential and on a system call the person recorded as having acted |
| `IRequestNamedOwner` | The organization or provider a product service's operation names in its path or body, which its outgoing calls carry where the session names none |
| `ContextScope`, `StatedContext` | The context of work that arrives without a request, such as a message taken off a queue |
| `IServiceIdentity` | Which service this process is, whether it is a product service (`IsProductService`), and the token audience it accepts (`Audience`) |
| `TokenAudience` | What a token's `aud` names: `Platform` (`bitwarden`) for a service's token; a product service's own name for a person's token |
| `EncryptedAttribute` | Declares that a resource member holds Protected Data, with its own maximum length |
| `PersonalDataAttribute` | Marks a resource attribute as personal data (an address, a name), which a cache read across owners may not hold |
| `IServiceExtension`, `ServiceExtensionAttribute` | How a platform package configures every service that references it |

## Encrypted members

A resource's string members are capped at 255 characters unless they declare a length, and an
encrypted string — an EncString, carrying its type, IV, ciphertext and MAC — is longer than that for
even a short secret. A member holding ciphertext says so, with the bound that replaces the default:

```csharp
/// <summary>The item's encrypted structure.</summary>
[Encrypted(MaxLength = 1_000_000)]
public required string Data { get; init; }
```

`MaxLength` is required; a declaration without a positive one throws the first time the resource
is bound or documented, rather than accepting every value. The service checks only the length and the published schema shows it as
`maxLength`. Nothing is decrypted or inspected — the service holds no key that could read it.
Plain strings keep the 255 default, which is what it is for.

Nothing here knows about HTTP hosting or HTTP calling. `Bitwarden.Server.Sdk.Service` implements
the serving side; `Bitwarden.Server.Sdk.Client` implements the calling side; neither references
the other.

## Remembering a fact for the rest of a request

`RequestMemo` holds what a request has already looked up about the acting person (who they are in
an organization, which grants they hold) so it isn't read again in the same request:

```csharp
var grants = await memo.RememberAsync("grants", organization, member, () => ReadGrantsAsync(member));
```

- **One request, never across callers.** Register it per request: `services.AddRequestMemo()`, or,
  in a service with a database, `services.AddRequestMemo<TContext>()` from Sdk.Data, which also
  forgets everything whenever the request saves, so a decision made after a write reads afresh.
- **Keyed by organization and person**, as well as the kind of fact, so nothing about one is ever
  answered for another.
- **Shared only with scopes the same request opens** (`memo.Share(requestMemo)`), such as one per
  organization in a read across several.
- A cache that outlives the request is a different design, with invalidation, and is not this.

## A library's exception that means one of the platform's

`IExceptionTranslator` says which platform exception a library's own failure means, so the service
answers it as that one. Sdk.Data registers one that answers Entity Framework's refused concurrent
write (`DbUpdateConcurrencyException`) as a `ConflictException`, `409`. The service's exception
handler asks every registered translator first; the log keeps the original exception.

## The service-owned cache's entry format

`OwnedCacheEntry` states once, for the service that writes an entry (Sdk.Caching) and the clients
that read it (Sdk.Client), how it sits in Redis: one hash under `{service}:{type}:{id}` with the
version (`v`), the owner (`o`), the resource (`d`), "nothing recorded" (`none`) or a tombstone
(`gone`); the configuration key of the service cache's connection; and the contract extension,
`x-bitwarden-cache`, that publishes a cacheable read.
