# Caller context

A request is about someone: an organization, a person, a provider, and the client and address it
came from. A product service reads that from the person's session. A domain service is called by
other services, so it reads it from the caller context they send, and believes it only as Identity
signed it.

## What the package does

- **`IContext`** (Bitwarden.Server.Sdk.Common) is the request's context in every kind of service:
  `OrganizationId`, `UserId`, `ProviderId`, `ClientIpAddress`, `DeviceType`, `DeviceIdentifier` and,
  for audit records only, `Credential`, `RecordedUserId` and `RecordedProviderId`. Your code reads
  the same interface whichever kind of service it runs in.
- **The caller-context headers** are `BitwardenHeaders.All`: `Bitwarden-Organization`,
  `Bitwarden-User`, `Bitwarden-Provider`, `Bitwarden-Context` (a closed, versioned JSON object) and
  `Bitwarden-Delegation` (the signed form of the other four).
- **A product service refuses all five** with `400` before authentication, whoever sends them.
- **A domain service believes them only from another service** (a token whose `client_id` is
  `internal.*`) and only as its context token says. From anyone else they are ignored and the
  context is empty.
- **Outgoing calls carry the context on.** The generated clients in Bitwarden.Server.Sdk.Client
  write the headers and exchange a context token from Identity for each callee: from the person's
  token at a product service, from the context token the call carried at a domain service. The
  person's token itself is never forwarded.

### What each kind of service believes

|                                | Domain service (default)                                                               | `[ProductService]`                                                 |
| ------------------------------ | -------------------------------------------------------------------------------------- | ------------------------------------------------------------------ |
| `IContext.UserId`              | The context token's user, from another service                                         | The validated token's `sub`                                        |
| `IContext.OrganizationId`      | The context token's organization                                                       | The token's signed `organization` claim                            |
| `IContext.ProviderId`          | The context token's provider                                                           | A provider session's `provider` claim                              |
| Address, device and credential | `Bitwarden-Context`, as the token signed it                                            | The connection's address, the `device` claim, the client's headers |
| A caller-context header        | Believed from an internal service with a valid context token; ignored from anyone else | Refused `400`, whoever sends it                                    |
| A token without a GUID `sub`   | Accepted                                                                               | Refused `401`, except a credential an operation accepts            |
| `ICallerRole`                  | Always `None`                                                                          | From the session, for the organization it names                    |

## Context tokens

A context token is a short-lived JWT of type `bitwarden-context+jwt`, obtained by RFC 8693 token
exchange and signed by Identity. A domain service accepts it only when it is:

- signed by a key, and issued by the issuer, that the service's bearer handler trusts;
- unexpired, and addressed (`aud`) to this service by its own name;
- issued to the caller presenting the service token (its `client_id` matches);
- marked `DELEGATED` (proven by a person's, organization's or machine account's own token) or
  `ASSERTED` (the calling service's own word), in `ctx_mode`.

Its context is then the token's. A plain header that is present must say the same thing. A call with
no context token and none of the headers has an empty context.

A domain service refuses `403` an **asserted** context that names a person, unless the operation
names the calling service in `[AcceptsAssertedContext]`. An asserted context naming nobody, and a
delegated one, are accepted everywhere.

## Example

A handler reads `IContext` and doesn't care where the values came from:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

[ApiTag(Description = "Widgets, which a person keeps.")]
public static class WidgetsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup("api/v1/widgets").WithStandards();
        widgets.MapGet("current", GetMyWidget);
        widgets.MapPost("{id}/actions/expire", Expire);
    }

    /// <summary>
    /// Get the person's own widget
    /// </summary>
    [Authorization(User = ContextRequirement.Required)]
    [StandardReadOne("current")]
    public static async Task<ResourceDocument<Widget>> GetMyWidget(
        IContext context, IWidgetStore store, CancellationToken cancellationToken)
    {
        // The same code in a domain or a product service: only where the values come from differs.
        var user = context.UserId!.Value;
        var widget = await store.FindForAsync(user, cancellationToken);

        return ReadOneResponse(widget ?? throw new NotFoundException("widget", "current"));
    }

    /// <summary>
    /// Expire a widget
    /// </summary>
    [AcceptsAssertedContext("The widget jobs expire widgets on their owners' behalf.", "widget-jobs")]
    [Authorization(User = ContextRequirement.Required)]
    [StandardAction]
    public static Task Expire(string id, IWidgetStore store, CancellationToken cancellationToken) =>
        store.ExpireAsync(id, cancellationToken);
}
```

`Expire` acts for a person when `widget-jobs` asserts one on its own authority. From any other
service, an asserted context naming a person is refused.

### What a product service refuses

A request to a product service carrying any caller-context header is refused before authentication,
pointing at the first header in the list:

```http
GET /api/v1/config HTTP/1.1
Bitwarden-User: 6f9619ff-8b86-d011-b42d-00c04fc964ff
```

```http
HTTP/1.1 400 Bad Request
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"400","code":"invalid-request","title":"The request cannot be handled.","detail":"The 'Bitwarden-User' header is not accepted by this service. It is a product service, called by people, so whom a request concerns is what the session token names.","source":{"header":"Bitwarden-User"}}]}
```

### What a domain service refuses

From another service, a call carrying caller context but no context token:

```http
GET /api/v1/widgets/current HTTP/1.1
Authorization: Bearer {internal.widget-portal token}
Bitwarden-User: 6f9619ff-8b86-d011-b42d-00c04fc964ff
```

```http
HTTP/1.1 401 Unauthorized
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"401","code":"unauthenticated","title":"The caller is not authenticated.","detail":"The call carries caller context but no context token. A service's calls carry the context Identity signed for them.","source":{"header":"Bitwarden-Delegation"}}]}
```

The other refusals use the same shape, each pointing at a header:

| The call                                                                      | Status | `source.header`        |
| ----------------------------------------------------------------------------- | ------ | ---------------------- |
| A context token that is forged, expired or for another service                | `401`  | `Bitwarden-Delegation` |
| A context token issued to another caller                                      | `401`  | `Bitwarden-Delegation` |
| A plain header that disagrees with the context token                          | `400`  | That header            |
| A caller-context header sent more than once                                   | `400`  | That header            |
| A malformed `Bitwarden-Context` object in the token                           | `400`  | `Bitwarden-Context`    |
| An asserted context naming a person, at an operation not admitting the caller | `403`  | `Bitwarden-Delegation` |

A person's token, a Send's token or no token at all, with the same headers, gets an empty context:
the headers are not read. The request then meets the operation's authorization like any other.

## What you decide

- **Strip the headers at the edge.** Every proxy in front of a service must strip
  `BitwardenHeaders.All` from what clients send. A product service refuses them anyway; a domain
  service reached through a public entry point relies on this.
- **Which callers may assert a person.** Name them in `[AcceptsAssertedContext]` with the reason, on
  each operation, so the exception is reviewed where it is read.
- **Your own access rules.** A context says whom a call concerns, not what the caller may do. An
  asserted context is only the calling service's word, so the receiving service still enforces its
  own rules.
- **Audit-only members stay audit-only.** `Credential`, `RecordedUserId`, `RecordedProviderId` and
  `UserOrganizationIds` must never drive authorization, a row filter or a cache.
- **Zero knowledge.** The context carries identifiers only, never a key or an encrypted value. Vault
  Data moves between services only as Protected Data, in request bodies.

## See also

- [Authentication and authorization](authentication-and-authorization.md)
- [Network and request limits](network-and-request-limits.md)
- [Defining a service](defining-a-service.md)
- [API standards](https://contributing.bitwarden.com/architecture/server/apis#authentication-and-authorization)
