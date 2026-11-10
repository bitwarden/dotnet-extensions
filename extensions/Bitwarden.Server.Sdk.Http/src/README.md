# Bitwarden.Server.Sdk.Http

Make a Bitwarden service's outbound HTTP calls safe: guard calls to addresses someone else chose, and trust the
deployment's own certificate authority for calls between services.

A service makes two kinds of outbound call. Some go to an address someone outside Bitwarden chose, such as an
organization's webhook or identity provider, which must never reach the deployment's private network. The rest go to
the deployment's other services over TLS, whose certificates often come from an internal authority the system doesn't
know. This package handles both, for any `HttpClient` the HTTP client factory makes. It has no ASP.NET Core
dependency, so a worker or a client library uses it as readily as a web service.

- **SSRF protection.** `AddSsrfProtection()` resolves each request's host before connecting, refuses a private,
  loopback, link-local or other reserved address, connects to the address it checked, and checks every redirect.
- **Cloud-only protection.** `AddCloudSsrfProtection()` guards a client in Bitwarden's cloud and leaves a self-hosted
  install's client as it was.
- **The same check, by hand.** `IPAddress.IsInternal()` is the check the guard makes, for validating an address when
  it is saved.
- **Internal TLS.** `InternalTls` names the certificate authorities trusted besides the system's, checks a server's
  certificate against them, and reads a server certificate given as settings.

## Getting started

```bash
dotnet new console -n WebhookSender
cd WebhookSender
dotnet add package Bitwarden.Server.Sdk.Http --prerelease
```

```csharp
using Bitwarden.Server.Sdk.Http;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddHttpClient("webhooks").AddSsrfProtection();

await using var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("webhooks");

try
{
    // A cloud metadata address, as a hostile webhook might name.
    await client.GetAsync("http://169.254.169.254/latest/meta-data/");
}
catch (SsrfProtectionException exception)
{
    Console.WriteLine(exception.Message);
}
```

`dotnet run` prints `Request to '169.254.169.254' was blocked because it resolves to an internal IP address.` Nothing
was sent.

## Examples

### Guard a client in the cloud only

An organization's identity provider is an address someone outside Bitwarden chose, but on a self-hosted install it
often runs on the same private network, where the operator chose it. `AddCloudSsrfProtection()` guards the client only
when `IBitwardenEnvironment.SelfHosted` (Bitwarden.Server.Sdk.Environment) is false:

```csharp
using Bitwarden.Server.Sdk.Environment.Setup;
using Microsoft.Extensions.DependencyInjection;

public static class IdentityProviderClients
{
    public static IServiceCollection AddIdentityProviderClients(this IServiceCollection services, bool selfHosted)
    {
        services.AddBitwardenEnvironment();
        services.Configure<SelfHostDetails>(details =>
        {
            if (selfHosted)
            {
                details.MakeSelfHost("standard");
            }
            else
            {
                details.MakeCloud();
            }
        });

        // Guarded in the cloud; left alone on a self-hosted install.
        services.AddHttpClient("identity-provider").AddCloudSsrfProtection();

        // A client that follows redirects itself: the guard checks each request it sends, and returns redirects.
        services.AddHttpClient("icons").AddSsrfProtection(followRedirects: false);

        return services;
    }
}
```

Where a service host registers the environment itself, the client registrations alone are enough.

### Refuse an internal address when it is saved

Check an address as it is saved as well as when it is called, so the person saving it hears why:

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

Name the root of the authority that issued your services' certificates, and check each server's certificate with
`InternalTls.Trusts`:

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
  for server authentication. A host name that doesn't match is never trusted.
- `LoadAuthorities()` throws, naming the setting, when a file is missing, can't be read or holds no certificate.
- `LoadServerCertificate()` reads `Tls:ServerCertificate` and `Tls:ServerKey`, returns null when neither is set, and
  throws, naming the settings and never a value, when only one is set, either can't be read, the key isn't the
  certificate's, or the certificate isn't valid now.
- A service host may apply the authorities to every client the HTTP client factory makes and serve its certificate
  itself. Do this yourself only in a host that doesn't.

## Configuration

The package reads no settings itself. `InternalTls` binds from the `Tls` section (`InternalTls.Section`) wherever a
host binds it:

| Setting                      | Default | What it does                                                                                              |
| ---------------------------- | ------- | --------------------------------------------------------------------------------------------------------- |
| `Tls:CertificateAuthorities` | None    | PEM files of root certificate authorities trusted for calls between services, besides the system's roots. |
| `Tls:RequireHttps`           | `false` | Whether calls to other services must use `https://`, and whether the host may listen only on `https://`.  |
| `Tls:ServerCertificate`      | None    | The host's server certificate, base64 of its DER encoding. With `Tls:ServerKey`.                          |
| `Tls:ServerKey`              | None    | Its private key, base64 of its PKCS #8 encoding. A secret.                                                |
| `Tls:HealthPort`             | None    | The one port a host may listen on in plain HTTP with `Tls:RequireHttps`, for its probes alone.            |

`AddCloudSsrfProtection()` reads `IBitwardenEnvironment.SelfHosted` as each client's handlers are built. The
host that binds `InternalTls` applies these settings itself.

## Security

- **TLS between services is a Secure Channel.** With a token on each call that the callee checks, it is a Trusted
  Channel. Name the same authority files on every service.
- **The guard is opt-in per client.** A client you don't guard can reach anything. Don't guard a client whose
  destination is the deployment's own: the guard would refuse it. A client guarded twice, by
  `ConfigureHttpClientDefaults` and by its own registration, gets one guard, with the first registration's
  `followRedirects`.
- **A name that can't be resolved is refused** with `SsrfProtectionException`, as an internal address is.
- **A proxy sees the checked address, not the host.** The guard addresses each request to the IP address it checked,
  with the host in the `Host` header, so a proxy's request line carries the address and a proxy bypass rule naming the
  host no longer matches. Once the response arrives, the request's and the response's `RequestUri` name the host
  again.
- **The guard covers redirects.** It turns automatic redirects off on the client's primary handler, follows up to 10
  itself, checks every hop, and never carries the original request's headers, `Authorization` among them, to the next
  hop. It never follows a redirect from `https://` to `http://`, which would leave TLS, and returns that redirect
  instead. A response a handler below it redirected is refused, not returned.
- **Self-hosted installs are unguarded with `AddCloudSsrfProtection()`.** Use `AddSsrfProtection()` for any
  destination a self-hosted install's users, rather than its operator, choose.
- **What leaves.** Data a service sends to a webhook leaves the Bitwarden Secure Environment. Send identifiers, never
  Vault Data, which the service holds only as Protected Data anyway.

## Requirements

- .NET 10.
- Bitwarden.Server.Sdk.Environment (for `AddCloudSsrfProtection()`) and Microsoft.Extensions.Http.

## Contributing

Every source file has a matching test class in `tests/Bitwarden.Server.Sdk.Http.Tests`. Run them from the repository
root:

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Http/tests/Bitwarden.Server.Sdk.Http.Tests
```

A change must keep:

- **Check before connecting.** Every address a name resolves to is checked, and the request connects to the one that
  was, so a name can't resolve elsewhere in between.
- **Every hop checked.** Automatic redirects stay off below the guard, whoever configures the primary handler, and no
  redirect leaves TLS.
- **No secret in a message.** A refusal names the setting, never a key, a certificate or a path's contents.
- **No ASP.NET Core dependency.**
