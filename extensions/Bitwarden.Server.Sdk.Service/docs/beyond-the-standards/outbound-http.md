# Outbound HTTP

A service makes two kinds of outbound call: to an address someone outside Bitwarden chose, such as
an organization's webhook or identity provider, and to the deployment's other services. The first
must not reach the private network (server-side request forgery). The second must run over a Trusted
Channel.

## What the package does

- **The SSRF guard.** `AddSsrfProtection()` on an `IHttpClientBuilder` resolves each request's host
  first and refuses an address in a private, loopback, link-local or other reserved range with
  `SsrfProtectionException`, before anything connects. It then connects to the address it checked,
  keeping the original host for TLS, so the name can't resolve elsewhere in between.
- **Redirects are checked hop by hop.** The guard turns off automatic redirects on the primary
  handler and follows them itself (up to 10), checking every hop.
- **`AddCloudSsrfProtection()`** guards only in Bitwarden's cloud and leaves a self-hosted install's
  client unguarded.
- **`IPAddress.IsInternal()`** (`IPAddressExtensions`) is the check the guard makes, for validating
  an address when it is saved.
- **Internal TLS.** `Tls:CertificateAuthorities` adds root authorities to the trust of every client
  the HTTP client factory makes and of the bearer handler's discovery and key fetches.
  `Tls:RequireHttps` refuses a plain-HTTP bearer authority at startup.

The extensions are in the `Microsoft.Extensions.DependencyInjection` namespace; the exception and
`IPAddressExtensions` in `Bitwarden.Server.Sdk.Service.Http`.

## Example

Register a guarded client for each destination someone outside Bitwarden chose:

```csharp
using Bitwarden.Server.Sdk.Service;

internal sealed class WidgetService(string[] args) : Service("widget-service", "Widget Service", args)
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        // An organization's webhook: an address someone outside Bitwarden chose. Guarded everywhere.
        services.AddHttpClient("widget-webhooks").AddSsrfProtection();

        // An organization's identity provider: guarded in the cloud only, since a self-hosted
        // identity provider often runs on the same private network.
        services.AddHttpClient("widget-identity-provider").AddCloudSsrfProtection();

        // A client that follows redirects itself; the guard checks each request it sends.
        services.AddHttpClient("widget-icons").AddSsrfProtection(followRedirects: false);

        services.AddSingleton<WidgetWebhooks>();
    }
}
```

A refused request surfaces as `SsrfProtectionException`. Turn it into an answer the caller can act
on rather than letting it become a `500`:

```csharp
using System.Net;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Http;

internal sealed class WidgetWebhooks(IHttpClientFactory clients, ILogger<WidgetWebhooks> logger)
{
    public async Task NotifyAsync(Uri webhook, string widgetId, CancellationToken cancellationToken)
    {
        var client = clients.CreateClient("widget-webhooks");

        try
        {
            // Only the identifier: never a widget's encrypted members.
            using var response = await client.PostAsJsonAsync(webhook, new { widgetId }, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (SsrfProtectionException)
        {
            // The host resolved to a private, loopback, link-local or reserved address.
            logger.LogWarning("Refused a widget webhook to an internal address.");
            throw new InvalidRequestException("The webhook's address is not reachable.", "/webhook");
        }
    }

    // The check the guard makes, for validating an address when it is saved.
    public static bool IsAllowed(IPAddress address) => !address.IsInternal();
}
```

### Calls to other services

A service serves HTTPS with Kestrel's own settings (`Kestrel:Endpoints:Https:*`). For its calls to
other services, name the authority that issued their certificates:

```json
{
    "Tls": {
        "CertificateAuthorities": ["/etc/bitwarden/ca/internal-root.pem"],
        "RequireHttps": true
    }
}
```

A server certificate is trusted when the system trusts it or it chains to a named root; a host name
that doesn't match is never trusted. TLS gives these calls a Secure Channel; with the service token
each call carries, and the callee's audience check, a Trusted Channel.

## Configuration

| Setting                      | Default | What it does                                                                                                                           |
| ---------------------------- | ------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| `Tls:CertificateAuthorities` | None    | PEM files of root certificate authorities trusted for outbound calls and the bearer handler's key fetches, besides the system's roots. |
| `Tls:RequireHttps`           | `false` | When true, the bearer authority must be `https://`, or the service does not start.                                                     |
| `globalSettings:selfHosted`  | `false` | When true, `AddCloudSsrfProtection()` clients are unguarded.                                                                           |

### Security trade-offs

- **Internal TLS.** Every service that calls another must trust the authority that issued the
  callee's certificate, so name the same authority files on every service. A file that can't be
  read, or holds no certificate, stops the service at startup.
- **Self-hosted identity providers.** `AddCloudSsrfProtection()` deliberately leaves a self-hosted
  install unguarded, where the operator chose the destinations. Use `AddSsrfProtection()` for any
  destination a self-hosted install's users, rather than its operator, choose.

## What you decide

- **Which clients are guarded.** The guard is opt-in per client. Add it to every client whose
  destination comes from an organization, a user or a document, and to none whose destination is the
  deployment's own.
- **A client with its own primary handler.** A client that sets its own primary handler after the
  platform keeps its own certificate trust, so it doesn't get `Tls:CertificateAuthorities`.
- **What leaves the service.** Data a service sends out, to a webhook especially, leaves the
  Bitwarden Secure Environment. Send identifiers, never Vault Data, which the service holds only as
  Protected Data anyway.

## See also

- [Authentication and authorization](authentication-and-authorization.md)
- [Configuration and self-hosting](configuration-and-self-hosting.md)
- [Network and request limits](network-and-request-limits.md)
- [API standards](https://contributing.bitwarden.com/architecture/server/apis)
