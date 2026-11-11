# Bitwarden.Server.Sdk.Client

Call another Bitwarden service with your service's own credential, the request's context, and every failure turned
into the exception it means.

A service publishes a client package for the others to call it with, and that package registers its client through
this one. A consumer calls the published package's `Add{Service}Client()` and configures nothing it deployed the
ordinary way. Every call then goes through one pipeline:

- **A credential.** The caller signs in as `internal.{CallerName}` with client credentials, asks for a token for the
  one service it calls, and presents it on every call. A token is renewed before it expires, so no call waits for it.
  With a private key configured, the client signs a client assertion in place of a shared secret.
- **Context.** The organization, acting user and provider the request concerns travel as headers, with the rest of the
  request's context in `Bitwarden-Context` and a context token the authorization server signs. `OutgoingCall` states
  whom one call is about where the request doesn't, or makes a call the service's own.
- **Failures as exceptions.** A refusal becomes the exception the callee threw (`NotFoundException`,
  `ConflictException` and the rest), and a callee that can't be reached or doesn't answer in time becomes a
  `ServiceUnavailableException` naming it.
- **Resilience.** One deadline per call, an idempotency key on every write, bounded retries of a callee that wasn't
  there, and a circuit breaker per callee.
- **Writes that span services.** Each write is reported to the operation running now, and `Undo` takes back the
  writes an operation made elsewhere when a later step is refused.
- **Product clients and cached reads.** `ProductClient` calls a product service with a token the caller hands it, and
  `ICachedReads<TClient>` answers a read from the callee's own Redis cache where the callee would have given the same
  answer.

## Getting started

```bash
dotnet new console -n GadgetService
cd GadgetService
dotnet add package Bitwarden.Server.Sdk.Client --prerelease
dotnet add package Microsoft.Extensions.Hosting
```

Add an `appsettings.json` beside `Program.cs`, copied to the output, naming this service, the service it calls and
where tokens are issued:

```json
{
    "Bitwarden": { "ServiceName": "gadget-service" },
    "Services": {
        "widget-service": {
            "BaseAddress": "http://localhost:5101",
            "IdentityUri": "http://localhost:5100",
            "CallerSecret": "not-a-real-secret"
        }
    }
}
```

```csharp
using Bitwarden.Server.Sdk.Errors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddBitwardenClient<WidgetClient, WidgetClient>("widget-service", ["widgets.read"]);

using var host = builder.Build();
await host.StartAsync();

try
{
    var widget = await host.Services.GetRequiredService<WidgetClient>().GetAsync("w-1");
    Console.WriteLine(widget);
}
catch (ServiceUnavailableException exception)
{
    Console.WriteLine($"{exception.Service}: {exception.Message}");
}

await host.StopAsync();

public sealed class WidgetClient(HttpClient http)
{
    public Task<string> GetAsync(string id, CancellationToken cancellationToken = default) =>
        http.GetStringAsync($"widgets/{id}", cancellationToken);
}
```

With nothing listening on those addresses, `dotnet run` prints
`identity: The identity service could not be reached for a token for calls to widget-service.`, after the HTTP client
factory's own log of the failed connection. The call never reached `widget-service`: the client asks for its token
first, and says that the authorization server is what failed. Leave out `BaseAddress` and the host refuses to start
with `The widget-service client is not configured. BaseAddress is missing; set Services:widget-service:BaseAddress, or
services:widget-service:https:0, services:widget-service:http:0, widget-service:baseUrl.`

## Examples

### Catch what the callee refused

A refusal arrives as the exception the callee threw, so a caller catches it by type:

```csharp
using Bitwarden.Server.Sdk.Errors;

public sealed class WidgetLookup(WidgetClient widgets)
{
    public async Task<string?> FindAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            return await widgets.GetAsync(id, cancellationToken);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}
```

An exception the caller doesn't catch reaches a service host's exception handler, which answers the caller's own
caller as the callee answered: a `NotFoundException` as `404`, a `ServiceUnavailableException` as `503`.

### State whom one call is about

A domain service sometimes asks about a person the request it is handling doesn't name. Wrap exactly that call:

```csharp
using Bitwarden.Server.Sdk.Client;

public sealed class WidgetReads(WidgetClient widgets)
{
    public async Task<string> ForPersonAsync(Guid person, string widget, CancellationToken cancellationToken)
    {
        // The request names somebody else, or nobody: this one call is about person.
        using (OutgoingCall.AboutUser(person))
        {
            return await widgets.GetAsync(widget, cancellationToken);
        }
    }

    public async Task<string> AsTheServiceAsync(Guid organization, string widget, CancellationToken cancellationToken)
    {
        // The service's own call about the organization: no user or provider is sent.
        using (OutgoingCall.AsSystem(organization))
        {
            return await widgets.GetAsync(widget, cancellationToken);
        }
    }
}
```

A statement covers one call: a second call inside it throws. A product service may only use
`AboutCreatedOrganization`, on an operation that creates an organization, and `WithoutProvider`. A service handling an
event makes each call its own (`AsSystem` and the other `AsSystem` statements) or names the person outright.

### Take back writes when a later step is refused

`Undo` records each write an operation made in another service, with what takes it back, and runs the next step
inside a guard:

```csharp
using Bitwarden.Server.Sdk.Client;
using Microsoft.Extensions.Logging;

public sealed class Grants(IWidgetGrants widgets, IGadgetStore gadgets, ILogger<Grants> logger)
{
    public async Task GrantAndDeleteAsync(Guid widget, Guid gadget, CancellationToken cancellationToken)
    {
        var undo = new Undo(logger);

        var grant = await widgets.GrantAsync(widget, cancellationToken);
        undo.Add($"grant {grant} on widget {widget}", token => widgets.RevokeAsync(widget, grant, token));

        // Refused, the grant is taken back and the refusal thrown. Any other failure may follow a delete that
        // landed, so the grant stays.
        await undo.GuardAsync(() => gadgets.DeleteAsync(gadget, cancellationToken), UndoOn.Refusal);
    }
}

public interface IWidgetGrants
{
    Task<Guid> GrantAsync(Guid widget, CancellationToken cancellationToken);

    Task RevokeAsync(Guid widget, Guid grant, CancellationToken cancellationToken);
}

public interface IGadgetStore
{
    Task DeleteAsync(Guid gadget, CancellationToken cancellationToken);
}
```

Undos run newest first, each on a 30-second deadline of its own rather than the request's token, and a failed undo is
logged by the text given to `Add`, so that text names identifiers only.

## Configuration

Each client's settings are looked up in order, most specific first. A missing `BaseAddress`, `CallerName`,
`IdentityUri`, or `CallerSecret` without a `CallerKey`, stops the host at startup, naming the keys that would have
supplied it. `{Name}` is the name the client registers the service under.

| Setting          | Keys tried, in order                                                                                                                                                 |
| ---------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `BaseAddress`    | `Services:{Name}:BaseAddress`, `services:{name}:https:0`, `services:{name}:http:0`, `{name}:baseUrl`                                                                 |
| `CallerName`     | `Services:{Name}:CallerName`, `Bitwarden:ServiceName`, else the entry assembly's name, lower case: its second part when it has more than one                         |
| `Audience`       | `Services:{Name}:Audience`, else `{Name}`                                                                                                                            |
| `IdentityUri`    | `Services:{Name}:IdentityUri`, `globalSettings:baseServiceUri:internalIdentity`, `globalSettings:baseServiceUri:identity`, `Authentication:Schemes:Bearer:Authority` |
| `CallerSecret`   | `Services:{Name}:CallerSecret`, `globalSettings:internalIdentityKey`                                                                                                 |
| `CallerKey`      | `Services:{Name}:CallerKey`, `Bitwarden:ClientKey`: a PKCS #8 private key, base64, P-256 or RSA of 2048 bits or more                                                 |
| `RequireHttps`   | `Tls:RequireHttps`: `BaseAddress` and `IdentityUri` must then be `https://`                                                                                          |
| Event actor rule | `OutgoingCall:RefuseEventActor` (`false`): refuse, rather than log, a call that carries an event's actor                                                             |

`Timeout` (30 seconds), `Retries` (2, at most 2), `RetryDelay` (200 ms), `BreakAfter` (5), `BreakFor` (5 seconds) and
`RefreshAt` (0.8 of a token's life) are set in code, through the `configure` argument of `AddBitwardenClient`.

Cached reads add these settings:

| Setting                                          | Default    | What it does                                                                     |
| ------------------------------------------------ | ---------- | -------------------------------------------------------------------------------- |
| `Caching:Owned:Redis:Configuration`              | None       | The Redis the callees' caches are in. Without it, every cached read is a miss.   |
| `Caching:Owned:AnswerSecret`                     | None       | The key for digests of text keys. Required outside `Development` with Redis set. |
| `Caching:Owned:Resources:{owner}:{name}:Enabled` | `true`     | `false` turns one resource's, index's or answer's cached reads off.              |
| `Caching:Owned:InProcess:Enabled`                | `true`     | `false` reads every index answer from Redis in full.                             |
| `Caching:Owned:InProcess:Lifetime`               | `00:00:30` | How long an index answer is held in the process.                                 |
| `Caching:Owned:InProcess:Capacity`               | `10000`    | How many index answers one client holds.                                         |

## Security

- **What the context headers prove.** `Bitwarden-Organization`, `Bitwarden-User`, `Bitwarden-Provider` and
  `Bitwarden-Context` are this service's word. `Bitwarden-Delegation` carries a context token the authorization server
  signs for this call's callee and this caller, exchanged from the request's own proof when the call carries the
  request's person, and from the service's own token otherwise. The client writes all of these itself and removes any
  a call site set.
- **A shared secret proves less than a key.** With `CallerSecret`, services that share one secret can't be told apart
  by it; the authorization server limits only what each client id may ask for. A `CallerKey` gives each service its
  own key, and the authorization server holds only its public half.
- **Tokens are per callee.** Each token names the one service it is for, so a token leaked at one callee is refused at
  every other.
- **Calls are a Secure Channel only over TLS.** Set `Tls:RequireHttps` so a plain address stops the host at startup:
  every `AddBitwardenClient` address and Identity's, and a product client's conventional address. A product client
  over a named client of your own is yours to check.
- **No secret in a log or a message.** Tokens, keys and assertions are never logged, an `Undo` failure is logged by
  the identifiers given to `Add`, and a `ProductAnswer`'s `ToString()` names its status, never its body, which may
  carry Protected Data.
- **A digest of a guessable key is confirmable.** `ReadAnswerAsync` digests a text key, such as an email address,
  before it reaches Redis. Without `Caching:Owned:AnswerSecret` the digest is unkeyed, so anyone who can read Redis
  and guesses the text can confirm it.
- **Cached reads are read-only.** Nothing in this package writes to Redis. A read is answered from the cache only where
  the callee would have answered this caller the same, by the checks the callee's contract publishes; any doubt is a
  miss.

## Requirements

- .NET 10.
- Bitwarden.Server.Sdk.Context, Bitwarden.Server.Sdk.Errors, Bitwarden.Server.Sdk.Http,
  Bitwarden.Server.Sdk.Idempotency.Abstractions, Bitwarden.Server.Sdk.Resources.Caching.Abstractions and
  Bitwarden.Server.Sdk.Sagas.
- Microsoft.Extensions.Http, StackExchange.Redis (cached reads) and Newtonsoft.Json (reading a cached answer into a
  type NSwag generated).
- An authorization server that issues client-credentials tokens with an `audience`, and RFC 8693 token exchange for
  context tokens.

## Contributing

Run the tests from the repository root. The cached-read tests start Redis with Testcontainers, so they need Docker:

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Client/tests/Bitwarden.Server.Sdk.Client.Tests
```

`NSwag/` holds three of NSwag's parameter templates, changed to move a date to UTC before it is formatted. They are
not part of the NuGet package: a client project built beside this repository names the folder with
`/TemplateDirectory`, with `/JsonConverters:Bitwarden.Server.Sdk.Client.UtcDateJsonConverter` and
`/ParameterDateTimeFormat:yyyy-MM-ddTHH:mm:ss.FFFFFFFZ`.

A change must keep:

- **One deadline per call**, covering the tokens the call obtains as well as the call, with the authorization server's
  failure said as its own and never as the callee's.
- **Retries only of what is safe to send twice**: a `GET` or `HEAD`, or a write carrying an idempotency key, and never
  an answer the callee gave itself.
- **Context written in one place.** `ContextHandler` writes every context header and removes whatever a call site set.
- **No secret in a log, a message or `ToString()`.**
