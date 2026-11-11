# Bitwarden.Server.Sdk.Http

## About

Outbound HTTP for Bitwarden services. Some calls go to an address someone outside Bitwarden chose, such as an
organization's webhook or identity provider, and must never reach the deployment's private network: this package adds
SSRF protection to any `HttpClient` the HTTP client factory makes. The rest go to the deployment's own services over
TLS, often with certificates from an internal authority the system doesn't know: `InternalTls` trusts that authority
for those calls.

It depends on Bitwarden.Server.Sdk.Environment and Microsoft.Extensions.Http, and not on ASP.NET Core, so a worker or a
client library uses it as readily as a web service.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Http
```

## Configuration

The package reads no settings itself. `InternalTls` binds from the `Tls` section (`InternalTls.Section`) wherever a
host binds it, and that host applies them:

| Setting                      | Default | What it does                                                                                              |
| ---------------------------- | ------- | --------------------------------------------------------------------------------------------------------- |
| `Tls:CertificateAuthorities` | None    | PEM files of root certificate authorities trusted for calls between services, besides the system's roots. |
| `Tls:RequireHttps`           | `false` | Whether calls to other services must use `https://`, and whether the host may listen only on `https://`.  |
| `Tls:ServerCertificate`      | None    | The host's server certificate, base64 of its DER encoding. With `Tls:ServerKey`.                          |
| `Tls:ServerKey`              | None    | Its private key, base64 of its PKCS #8 encoding. A secret.                                                |
| `Tls:HealthPort`             | None    | The one port a host may listen on in plain HTTP with `Tls:RequireHttps`, for its probes alone.            |

Name an intermediate authority's root in `Tls:CertificateAuthorities`, not the intermediate alone.

## Usage

### Guard a client whose destination someone else chose

Add the guard to the client's registration, and handle the refusal where you send:

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
            // The address is internal or can't be resolved. Nothing was sent.
            return false;
        }
    }
}
```

The guard resolves each request's host before connecting. It throws `SsrfProtectionException` when the name can't be
resolved or any address it resolves to is private, loopback, link-local or otherwise reserved, such as
`Request to '169.254.169.254' was blocked because it resolves to an internal IP address.` Otherwise it connects to the
address it checked, so the name can't resolve elsewhere in between, and keeps the host for TLS.

Redirects:

- The guard turns automatic redirects off on the client's primary handler, whoever configures it, follows up to 10
  itself and checks every hop.
- It never carries the original request's headers, `Authorization` among them, to the next hop.
- It returns, rather than follows, a redirect from `https://` to `http://`, which would leave TLS, and a redirect to a
  scheme other than `http` or `https`.
- A response that a handler below it redirected is refused, not returned.
- Following redirects, the guard buffers the request body in memory once, so a `307` or `308` can send it again. Use
  `followRedirects: false` for a client that streams large bodies.

Watch for:

- **The guard is opt-in per client.** A client you don't guard can reach anything. Don't guard a client whose
  destination is the deployment's own: the guard would refuse it. It has no allowlist.
- **Guarding twice.** A client guarded by both `ConfigureHttpClientDefaults` and its own registration gets one guard,
  with the first registration's `followRedirects`. Every client's defaults apply first.
- **A proxy sees the checked address, not the host.** Each request is addressed to the IP address the guard checked,
  with the host in the `Host` header, so a proxy's request line carries the address and a proxy bypass rule naming the
  host no longer matches. Once the response arrives, the request's and the response's `RequestUri` name the host
  again.
- **What leaves.** Data a service sends to a webhook leaves the Bitwarden Secure Environment. Send identifiers, never
  Vault Data, which the service holds only as Protected Data anyway.

### Guard a client in the cloud only

An organization's identity provider is an address someone outside Bitwarden chose, but on a self-hosted install it
often runs on the same private network, where the operator chose it. `AddCloudSsrfProtection()` guards the client only
when `IBitwardenEnvironment.SelfHosted` is false, read as each client's handlers are built. It needs
`IBitwardenEnvironment`, which `AddBitwardenEnvironment()` registers; a service host usually registers it already.

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

A self-hosted install's client is unguarded. Use `AddSsrfProtection()` for any destination a self-hosted install's
users, rather than its operator, choose.

### Refuse an internal address when it is saved

`IPAddress.IsInternal()` is the check the guard makes. Check an address as it is saved as well as when it is called,
so the person saving it hears why:

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
guard does. `IdnHost` is the name's ASCII form, which DNS needs. A name can resolve differently later, so this doesn't
replace the guard on the client.

### Trust an internal certificate authority

TLS between services is a Secure Channel. With a token on each call that the callee checks, it is a Trusted Channel.
Name the root of the authority that issued your services' certificates, the same files on every service, and check
each server's certificate with `InternalTls.Trusts`:

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

## Types

| Type                              | Purpose                                                                 |
| --------------------------------- | ----------------------------------------------------------------------- |
| `HttpClientBuilderSsrfExtensions` | `AddSsrfProtection()` and `AddCloudSsrfProtection()` on an HTTP client  |
| `SsrfProtectionException`         | A request the guard refused                                             |
| `IPAddressExtensions`             | `IsInternal()`: whether an address is in a private or reserved range    |
| `InternalTls`                     | The `Tls` settings, the authorities trusted, and the server certificate |
