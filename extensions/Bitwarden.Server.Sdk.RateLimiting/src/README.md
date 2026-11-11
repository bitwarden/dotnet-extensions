# Bitwarden.Server.Sdk.RateLimiting

Limit how often one address may call a Bitwarden service, counted in the process or in a Redis every instance shares.

Rules come from settings, in the `IpRateLimitOptions` shape Bitwarden's servers already use. A request over a limit is
refused before anything else answers it, with `429`, a `Retry-After` header and the standard error document.

- **Rules from settings.** Each rule limits requests matching an endpoint pattern, such as `post:/connect/token`, to a
  number per period. Addresses and endpoints can be left out.
- **Fixed windows, most restrictive first.** Of the rules a request matches, the lowest limit for each period counts,
  shortest period first, and counting stops at the first over its limit.
- **Sign-ins apart from other grants.** A rule can count only token requests naming certain grant types.
- **Shared counts.** With a Redis connection string, every instance of a service counts in one place, under a hash of
  the rule, the endpoint and the address. Without one, each process counts on its own.
- **Your own refusal.** A route whose answer shape somebody else fixed, such as an OAuth token endpoint, can refuse in
  that shape.
- **Your own limits.** `IRateLimitCounters` counts anything else you want to limit.

Bitwarden.Server.Sdk.Service registers and orders it for every service. Reference it directly only to compose a host
yourself, or to count limits of your own.

## Getting started

A host that composes the package itself registers it, and adds its middleware where Bitwarden.Server.Sdk.Service does:
after CORS, so a refusal carries the headers a browser needs, and before authentication.

```bash
dotnet new web -n WidgetService
cd WidgetService
dotnet add package Bitwarden.Server.Sdk.RateLimiting --prerelease
```

```csharp
using Bitwarden.Server.Sdk.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddIpRateLimiting();

var app = builder.Build();

app.UseIpRateLimiting();

app.MapGet("/api/v1/widgets", () => "widgets");

app.Run();
```

Add a rule in `appsettings.json`:

```json
{
    "IpRateLimitOptions": {
        "GeneralRules": [
            { "Endpoint": "get:/api/v1/widgets", "Period": "10s", "Limit": 2 }
        ]
    }
}
```

`dotnet run`, then `GET /api/v1/widgets` three times within ten seconds. The third is refused `429` with
`Retry-After: 10` and an error document whose code is `too-many-requests`.

Behind a proxy, every request comes from the proxy's address unless the host takes the client's address from a
trusted proxy first: Bitwarden.Server.Sdk.Authorization's `UseForwardedClientAddress()`, ahead of this.

## Examples

### Refuse in another shape

Register an `IIpRateLimitRefusal`. It writes the body; the status and `Retry-After` are already set:

```csharp
using System.Net.Mime;
using Bitwarden.Server.Sdk.RateLimiting;

public sealed class TokenEndpointRefusal : IIpRateLimitRefusal
{
    public Task WriteAsync(HttpContext http, string message)
    {
        http.Response.ContentType = MediaTypeNames.Application.Json;
        return http.Response.WriteAsJsonAsync(new TokenError("slow_down", message), http.RequestAborted);
    }
}

public sealed record TokenError(string error, string error_description);

public static class TokenEndpointRefusalRegistration
{
    // Registered after AddIpRateLimiting(), which only tries to add its own, so this one is used.
    public static IServiceCollection AddTokenEndpointRefusal(this IServiceCollection services) =>
        services.AddSingleton<IIpRateLimitRefusal, TokenEndpointRefusal>();
}
```

The refusal you register replaces the standard one for every route.

### Count limits of your own

The package limits per address. To limit per person, per organization or anything else, count in `IRateLimitCounters`,
the same store the address limits use:

```csharp
using Bitwarden.Server.Sdk.RateLimiting;

public sealed class InvitationAllowance(IRateLimitCounters counters)
{
    public ValueTask<bool> TryTakeAsync(Guid organization, CancellationToken cancellationToken) =>
        counters.TryTakeAsync($"invitations\n{organization}", limit: 100, period: TimeSpan.FromHours(1), cancellationToken);
}
```

`TryTakeAsync` is true while the window's count is within the limit, and a limit below 1 counts as 1. A window opens with its first request and lasts
the period. `IpRateLimitRules` applies the rule choice described above to rules you define.

## Configuration

| Setting                                                              | Default | What it does                                                                                                           |
| -------------------------------------------------------------------- | ------- | ---------------------------------------------------------------------------------------------------------------------- |
| `IpRateLimitOptions:Enabled`                                         | `true`  | Whether limits apply. Off when self-hosted, and with no rules, whatever this says.                                     |
| `IpRateLimitOptions:GeneralRules`                                    | None    | The rules: `Endpoint` (default `*`), `Period` (default `1m`), `Limit` (default `0`, refusing every request), `Grants`. |
| `IpRateLimitOptions:EnableEndpointRateLimiting`                      | `true`  | Counts each method and path apart. When false, only `*` rules apply, counting all of an address's requests.            |
| `IpRateLimitOptions:IpWhitelist`                                     | None    | Addresses and CIDR networks never limited.                                                                             |
| `IpRateLimitOptions:EndpointWhitelist`                               | None    | `verb:path` patterns never limited.                                                                                    |
| `IpRateLimitOptions:HttpStatusCode`                                  | `429`   | The status a request over a limit is refused with.                                                                     |
| `globalSettings:distributedIpRateLimiting:redisConnectionString`     | None    | A Redis where every instance keeps one shared count. Unset, each process counts on its own.                            |
| `globalSettings:distributedIpRateLimiting:enabled`                   | `true`  | Whether a configured Redis is used.                                                                                    |
| `globalSettings:distributedIpRateLimiting:maxRedisTimeoutsThreshold` | `10`    | How many Redis failures within the window set Redis aside, letting requests through unlimited.                         |
| `globalSettings:distributedIpRateLimiting:slidingWindowSeconds`      | `120`   | The window, in seconds, those failures are counted in.                                                                 |
| `globalSettings:selfHosted`                                          | `false` | When true, there are no limits.                                                                                        |

- **Matching.** `Endpoint` is `*` or `verb:path`, with `*` for any run of characters and `?` for any one. It is
  matched without case against the path in lower case, without a trailing slash and with repeated slashes collapsed.
  `Period` is a number followed by `s`, `m`, `h` or `d`.
- **Grants.** A rule with `Grants` counts a token request only when its form names exactly one `grant_type` and it is
  one of them. A request naming none, or several, counts as it would against a rule without grants.
- **Your service network.** List it in `IpWhitelist`: services call each other and fetch their own tokens.

[Network and request limits](../../Bitwarden.Server.Sdk.Service/docs/running-a-service/network-and-request-limits.md) in
Bitwarden.Server.Sdk.Service's guide shows these settings in a service, beside trusted proxies.

## Security

- **Bad settings stop the host.** A `Period` that doesn't parse, or an `IpWhitelist` entry that is neither an address
  nor a CIDR network, throws when the middleware is built, so the host doesn't start.
- **Shared counts hold hashed addresses.** Keys are `{service}:ip-rate-limit:{SHA-256}` and expire with their window.
  The hash is unsalted and the IPv4 address space is small, so anyone who can read the Redis can recover the
  addresses: protect it as you would the logs, and keep its connection string, which usually carries a password, in
  secrets.
- **Redis failing lets requests through.** While Redis is disconnected, or when a call to it fails, the request goes
  on unlimited. After `maxRedisTimeoutsThreshold` failures within `slidingWindowSeconds`, Redis is set aside until the
  window closes. An outage never refuses requests, and never limits them either.
- **Self-hosted installs have no limits.** A self-hosted operator who needs them puts them in a proxy in front of the
  service.

## Requirements

- .NET 10 and ASP.NET Core.
- Bitwarden.Server.Sdk.Context, Bitwarden.Server.Sdk.Errors and StackExchange.Redis.

## Contributing

Every type with logic has a test class of the same name in the package's `tests` folder; the Redis counters' start a
Redis container, so they need Docker. Run them from the repository root:

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.RateLimiting/tests/Bitwarden.Server.Sdk.RateLimiting.Tests
```

A change must keep:

- **The settings' shape.** `IpRateLimitOptions` and `globalSettings:distributedIpRateLimiting` are read from the
  configuration operators already have.
- **Nothing a client writes chooses its address.** The address is the connection's, as a trusted proxy reported it.
  With `EnableEndpointRateLimiting`, the path is part of the bucket, so a client choosing paths spreads its requests
  across buckets; only a `*` rule without it counts all of an address's requests together.
- **No address in clear in a shared store.**
