# Bitwarden.Server.Sdk.Http

**Audience:** engineers writing a Bitwarden service, worker or client library that makes outbound HTTP calls.

## About

Outbound HTTP for Bitwarden services: SSRF protection for any `HttpClient` the HTTP client factory makes, and
`InternalTls` to trust the deployment's own certificate authority for calls between services. It depends on
Bitwarden.Server.Sdk.Environment and Microsoft.Extensions.Http, and not on ASP.NET Core, so a worker or a client
library uses it as readily as a web service.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Http
```

## Configuration

The package reads no settings itself, and applies none. `InternalTls` binds from the `Tls` section
(`InternalTls.Section`) wherever a host binds it, and only a host that applies a setting gives it effect: in a host
that doesn't, `Tls:RequireHttps` set to `true` changes nothing. No host in this repository applies `Tls:RequireHttps`,
`Tls:ServerCertificate`, `Tls:ServerKey` or `Tls:HealthPort` yet.

| Setting                      | Default | What it does                                                                                              |
| ---------------------------- | ------- | --------------------------------------------------------------------------------------------------------- |
| `Tls:CertificateAuthorities` | None    | PEM files of root certificate authorities trusted for calls between services, besides the system's roots. |
| `Tls:RequireHttps`           | `false` | Whether calls to other services must use `https://`, and whether the host may listen only on `https://`.  |
| `Tls:ServerCertificate`      | None    | The host's server certificate, base64 of its DER encoding. With `Tls:ServerKey`.                          |
| `Tls:ServerKey`              | None    | Its private key, base64 of its PKCS #8 encoding. A secret: see below.                                     |
| `Tls:HealthPort`             | None    | The one port a host may listen on in plain HTTP with `Tls:RequireHttps`, for its probes alone.            |

`Tls:ServerKey` must reach the host through its secrets provider or an environment variable, never a file in a
repository. It stays in `IConfiguration` as a string for the life of the process, so never show the `Tls` section in a
view of the configuration.

## Key concepts

- **Two kinds of destination.** Some calls go to an address someone outside Bitwarden chose, such as an
  organization's webhook or identity provider, and must never reach the deployment's private network: guard those
  clients. The rest go to the deployment's own services over TLS, often with certificates from an internal authority
  the system doesn't know: `InternalTls` trusts that authority for those calls.
- **The guard checks before connecting.** It resolves each request's host and throws `SsrfProtectionException` when
  the name can't be resolved or any address it resolves to is private, loopback, link-local or otherwise reserved, such
  as `Request to '169.254.169.254' was blocked because it resolves to an internal IP address.` Otherwise it connects to
  the address it checked, so the name can't resolve elsewhere in between. The request keeps its host name, so TLS and
  telemetry see the name.
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
- It never carries the original request's headers, `Authorization` among them, to the next hop. It runs below every
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

Name the root of the authority that issued your services' certificates, the same files on every service, and check
each server's certificate with `InternalTls.Trusts`. `InternalCalls.cs`:

```csharp
using Bitwarden.Server.Sdk.Http;
using Microsoft.Extensions.Configuration;

public static class InternalCalls
{
    public static SocketsHttpHandler Handler(IConfiguration configuration)
    {
        var tls = configuration.GetSection(InternalTls.Section).Get<InternalTls>() ?? new InternalTls();
        var authorities = tls.LoadAuthorities();

        return new SocketsHttpHandler
        {
            SslOptions =
            {
                RemoteCertificateValidationCallback = (_, certificate, chain, errors) =>
                    InternalTls.Trusts(authorities, certificate, chain, errors),
            },
        };
    }
}
```

- A certificate is trusted when the system trusts it, or when it chains to a named root, is valid now and is allowed
  for server authentication. A host name that doesn't match is never trusted. Revocation is checked, and certificates
  are downloaded, only as the system's chain was set to.
- `LoadAuthorities()` throws, naming the setting, when a file is missing, can't be read or holds no certificate.
- `LoadServerCertificate()` reads `Tls:ServerCertificate` and `Tls:ServerKey`, returns null when neither is set, and
  throws, naming the settings and never a value, when only one is set, either can't be read, the key isn't the
  certificate's, or the certificate isn't valid now.
- A service host may apply the authorities to every client the HTTP client factory makes and serve its certificate
  itself. Do this yourself only in a host that doesn't.

## Gotchas and constraints

- **The guard is opt-in per client.** A client you don't guard can reach anything. Don't guard a client whose
  destination is the deployment's own: the guard would refuse it. It has no allowlist.
- **A self-hosted install's client is unguarded** with `AddCloudSsrfProtection()`. Use `AddSsrfProtection()` for any
  destination a self-hosted install's users, rather than its operator, choose.
- **The client sends with a `SocketsHttpHandler`.** The guard pins each connection through the handler's
  `ConnectCallback`, turns off its automatic redirects and its cookies, and fixes its proxy when the client is created.
  The factory's own primary handler is one. A primary handler you configure must be a `SocketsHttpHandler` without a
  `ConnectCallback` of its own: creating the client fails, naming it, for an `HttpClientHandler`, a `DelegatingHandler`
  wrapping a `SocketsHttpHandler`, or any other handler. Set the same settings on a `SocketsHttpHandler` instead, and add
  other handlers with `AddHttpMessageHandler`.
- **No HTTP/3.** QUIC connections don't go through `ConnectCallback`, so the guard refuses a request for HTTP/3 or one
  whose version policy allows a higher version.
- **Don't show a refusal's message to a user.** It says whether a name resolves, and whether it resolves to an
  internal address, which maps the internal network for whoever chose the address. Catch `SsrfProtectionException`
  itself: it isn't an `HttpRequestException`.
- **Guarding twice.** A client guarded by both `ConfigureHttpClientDefaults` and its own registration gets one guard,
  with the first registration's `followRedirects`. Every client's defaults apply first.
- **Following redirects buffers the body.** Once the first hop is checked, the guard buffers the request body and keeps
  a copy, so it is held in memory twice, with no limit, and a `307` or `308` can send it again. Use
  `followRedirects: false` for a client that sends large or streamed bodies.
- **A proxy sees the checked address, not the host.** A request that goes through a proxy is addressed to the IP
  address the guard checked, with the host in the `Host` header, so the proxy has no name to resolve differently; an
  `https` request's tunnel is opened to that address. The proxy's request line, and the client's telemetry for that
  request, carry the address. The proxy is the client's own, or the process's default one when the client is created:
  a default proxy changed later doesn't apply to it. A proxy bypass rule naming
  the host still applies: such a request connects directly, pinned to the checked address. Once the response arrives,
  the request's and the response's `RequestUri` name the host again.
- **A check when saving doesn't replace the guard.** A name can resolve differently later, so keep the guard on the
  client.
- **What leaves.** Data a service sends to a webhook leaves the Bitwarden Secure Environment. Send identifiers, never
  Vault Data, which the service holds only as Protected Data anyway.
- **Name an intermediate authority's root** in `Tls:CertificateAuthorities`, not the intermediate alone.

## Types

| Type                              | Purpose                                                                 |
| --------------------------------- | ----------------------------------------------------------------------- |
| `HttpClientBuilderSsrfExtensions` | `AddSsrfProtection()` and `AddCloudSsrfProtection()` on an HTTP client  |
| `SsrfProtectionException`         | A request the guard refused                                             |
| `IPAddressExtensions`             | `IsInternal()`: whether an address is in a private or reserved range    |
| `InternalTls`                     | The `Tls` settings, the authorities trusted, and the server certificate |
