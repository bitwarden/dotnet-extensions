# Bitwarden.Server.Sdk.Http

**Audience:** engineers writing a Bitwarden service, worker or client library that makes outbound HTTP calls.

## About

Outbound HTTP for Bitwarden services: SSRF protection for any `HttpClient` the HTTP client factory makes, and
`InternalTls` to trust the deployment's own certificate authority, and only it, for calls between services. It depends on
Bitwarden.Server.Sdk.Environment and Microsoft.Extensions.Http, and not on ASP.NET Core, so a worker or a client
library uses it as readily as a web service.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Http
```

## Configuration

The package reads no settings itself, and applies none. `InternalTls` binds from the `Tls` section
(`InternalTls.Section`) wherever you bind it, and applies only to the clients you give its chain policy.

| Setting                      | Default | What it does                                                                                                             |
| ---------------------------- | ------- | ------------------------------------------------------------------------------------------------------------------------ |
| `Tls:CertificateAuthorities` | None    | PEM files of the root certificate authorities a client calling your own services trusts, in place of the system's roots. |

## Key concepts

- **Two kinds of destination.** Some calls go to an address someone outside Bitwarden chose, such as an
  organization's webhook or identity provider, and must never reach the deployment's private network: guard those
  clients. The rest go to the deployment's own services over TLS, often with certificates from an internal authority
  the system doesn't know: `InternalTls` trusts that authority, and only it, for those calls. Keep the two apart: a
  client is one kind or the other, never both.
- **The guard checks before connecting.** It resolves each request's host and throws `SsrfProtectionException` when
  the name can't be resolved or any address it resolves to is private, loopback, link-local or otherwise reserved, such
  as `Request to '169.254.169.254' was blocked because it resolves to an internal IP address.` Otherwise it sends the
  request to the address it checked, with the host name in the `Host` header, so the name can't resolve elsewhere in
  between. TLS takes the name from the `Host` header, so the certificate is checked against the host name.
- **Channels between services.** TLS between services is a Secure Channel. With a token on each call that the callee
  checks, it is a Trusted Channel.

## Usage

### Guard a client whose destination someone else chose

Add the guard to the client's registration, and handle the refusal where you send. `WebhookClients.cs`:

```csharp
using Bitwarden.Server.Sdk.Http;
using Microsoft.Extensions.DependencyInjection;

public static class WebhookClients
{
    public static IServiceCollection AddWebhookClients(this IServiceCollection services)
    {
        // Follows up to 10 redirects, checking each one.
        services.AddHttpClient("webhooks").AddSsrfProtection();

        // A client that follows redirects itself: each request it sends is checked, and redirects are returned.
        services.AddHttpClient("icons").AddSsrfProtection(followRedirects: false);

        return services;
    }
}

public class WebhookSender(IHttpClientFactory clients)
{
    public async Task<bool> SendAsync(Uri webhook, HttpContent payload, CancellationToken cancellationToken)
    {
        var client = clients.CreateClient("webhooks");
        try
        {
            using var response = await client.PostAsync(webhook, payload, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (SsrfProtectionException)
        {
            // The address, or a redirect's, is internal or can't be resolved. When it is a redirect's, the
            // request that was redirected has already been sent.
            return false;
        }
    }
}
```

Redirects:

- The guard turns automatic redirects off on the client's primary handler, whoever configures it, follows up to 10
  itself and checks every hop.
- It never carries the original request's headers, `Authorization` among them, to the next hop. That includes headers
  set on the client, such as a `User-Agent` or `Accept` in `DefaultRequestHeaders` or `ConfigureHttpClient`:
  `HttpClient` copies them onto the request before any handler runs, so only the first hop has them. It runs below every
  handler the client adds, whenever it was added, so a handler that attaches a credential runs once for each request
  you send and never sees a redirect hop. Don't attach a credential in a handler a filter adds, which may run below it.
- It returns, rather than follows, a redirect from `https://` to `http://`, which would leave TLS, and a redirect to a
  scheme other than `http` or `https`.

### Guard a client in the cloud only

An organization's identity provider is an address someone outside Bitwarden chose, but on a self-hosted install it
often runs on the same private network, where the operator chose it. `AddCloudSsrfProtection()` guards the client only
when `IBitwardenEnvironment.SelfHosted` is false, read as each client's handlers are built. It needs
`IBitwardenEnvironment`, which `AddBitwardenEnvironment()` registers; a service host usually registers it already.
`IdentityProviderClients.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

public static class IdentityProviderClients
{
    public static IServiceCollection AddIdentityProviderClients(this IServiceCollection services)
    {
        services.AddBitwardenEnvironment();

        // Guarded in the cloud; left as it was, following redirects itself, on a self-hosted install.
        services.AddHttpClient("identity-provider").AddCloudSsrfProtection();

        return services;
    }
}
```

### Refuse an internal address when it is saved

`IPAddress.IsInternal()` is the check the guard makes. Check an address as it is saved as well as when it is called,
so the person saving it hears why. `WebhookAddresses.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using Bitwarden.Server.Sdk.Http;

public static class WebhookAddresses
{
    public static async Task<bool> IsAllowedAsync(Uri webhook, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(webhook.Host, out var literal))
        {
            return !literal.IsInternal();
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(webhook.IdnHost, cancellationToken);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentOutOfRangeException)
        {
            // The name can't be resolved, or is longer than DNS allows.
            return false;
        }

        return addresses.Length > 0 && !addresses.Any(address => address.IsInternal());
    }
}
```

`Dns.GetHostAddressesAsync` throws, rather than returning nothing, for a name that can't be resolved
(`SocketException`) or is longer than 255 characters (`ArgumentOutOfRangeException`); the sample refuses both, as the
guard does. `IdnHost` is the name's ASCII form, which DNS needs.

### Trust an internal certificate authority

Name the root of the authority that issued your services' certificates, the same files on every service, and give
`InternalTls.ChainPolicy` to each client that calls your own services, and to no other. `InternalCalls.cs`:

```csharp
using Bitwarden.Server.Sdk.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class InternalCalls
{
    public static IServiceCollection AddInternalCalls(this IServiceCollection services, IConfiguration configuration)
    {
        var tls = configuration.GetSection(InternalTls.Section).Get<InternalTls>() ?? new InternalTls();
        var authorities = tls.LoadAuthorities();

        // Only the clients that call your own services, never a guarded one.
        services.AddHttpClient("widget-service")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                SslOptions = { CertificateChainPolicy = InternalTls.ChainPolicy(authorities) },
            });

        return services;
    }
}
```

- A certificate is trusted only when it chains to a named root: the system's roots aren't, so a certificate a public
  authority issued for one of your services' names is refused. `SslStream` still checks that it names the host
  called, is valid now and is allowed for server authentication.
- **Revocation isn't checked.** An internal authority may publish no revocation list, so a certificate's lifetime is
  what limits a stolen key's use: keep it short.
- No certificate is downloaded: a server must send the intermediates between its certificate and the root.
- `ChainPolicy` throws `ArgumentException` when no authority is named, rather than trusting nothing in silence.
- `LoadAuthorities()` throws, naming the setting, when a file is missing, can't be read or holds no certificate.

## Gotchas and constraints

- **The guard is opt-in per client.** A client you don't guard can reach anything. Don't guard a client whose
  destination is the deployment's own: the guard would refuse it. It has no allowlist.
- **A self-hosted install's client is unguarded** with `AddCloudSsrfProtection()`. Use `AddSsrfProtection()` for any
  destination a self-hosted install's users, rather than its operator, choose.
- **The client sends with a `SocketsHttpHandler`.** The guard turns off its automatic redirects and its cookies when
  the client is created: cookies would be kept by address, which many hosts can share. The factory's own primary
  handler is one. A primary handler you configure must be a `SocketsHttpHandler`: creating the client fails, naming it,
  for an `HttpClientHandler`, a `DelegatingHandler` wrapping a `SocketsHttpHandler`, or any other handler. Set the same
  settings on a `SocketsHttpHandler` instead, and add other handlers with `AddHttpMessageHandler`.
- **No HTTP/3.** A server's `Alt-Svc` header can name another host, which the handler would reach over QUIC by
  resolving that name itself, unchecked. The guard refuses a request for HTTP/3 or one whose version policy allows a
  higher version.
- **Don't show a refusal's message to a user.** It says whether a name resolves, and whether it resolves to an
  internal address, which maps the internal network for whoever chose the address. Catch `SsrfProtectionException`
  itself: it isn't an `HttpRequestException`.
- **Guarding twice.** A client guarded by both `ConfigureHttpClientDefaults` and its own registration gets one guard,
  with the first registration's `followRedirects`. Every client's defaults apply first.
- **Following redirects buffers the body.** Once the first hop is checked, the guard buffers the request body and keeps
  a copy, so it is held in memory twice, with no limit, and a `307` or `308` can send it again. Use
  `followRedirects: false` for a client that sends large or streamed bodies.
- **The request is addressed to the checked address.** Below the guard, the request's `RequestUri` names the IP
  address, with the host in the `Host` header. Telemetry's `url.full` and the factory's inner logging handler record
  the address; `server.address` records the host. A proxy is given the address, so it has no name to resolve
  differently, and an `https` request's tunnel is opened to it. A proxy bypass rule applies to the address, not the
  host: one naming only the host no longer matches. Once the response arrives, the request's and the response's
  `RequestUri` name the host again.
- **A check when saving doesn't replace the guard.** A name can resolve differently later, so keep the guard on the
  client.
- **What leaves.** Data a service sends to a webhook leaves the Bitwarden Secure Environment. Send identifiers, never
  Vault Data, which the service holds only as Protected Data anyway.
- **Name an intermediate authority's root** in `Tls:CertificateAuthorities`, not the intermediate alone.
- **Never give the internal authority to a guarded client.** A guarded client calls addresses someone outside
  Bitwarden chose, which the internal authority has no business vouching for.

## Types

| Type                              | Purpose                                                                          |
| --------------------------------- | -------------------------------------------------------------------------------- |
| `HttpClientBuilderSsrfExtensions` | `AddSsrfProtection()` and `AddCloudSsrfProtection()` on an HTTP client           |
| `SsrfProtectionException`         | A request the guard refused                                                      |
| `IPAddressExtensions`             | `IsInternal()`: whether an address is in a private or reserved range             |
| `InternalTls`                     | The `Tls` settings, and the chain policy trusting the internal authorities alone |
