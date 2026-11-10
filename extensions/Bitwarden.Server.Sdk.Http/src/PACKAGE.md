# Bitwarden.Server.Sdk.Http

## About

Outbound HTTP for Bitwarden services: SSRF protection for clients whose destination someone outside Bitwarden chose,
such as an organization's webhook or identity provider, and trust of a deployment's internal certificate authority for
calls between services.

It depends on Bitwarden.Server.Sdk.Environment and Microsoft.Extensions.Http, and not on ASP.NET Core.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Http
```

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

`AddCloudSsrfProtection()` reads `IBitwardenEnvironment.SelfHosted`. The host that binds `InternalTls` applies these
settings itself.

## Getting started

Guard each client whose destination someone else chose:

```csharp
using Microsoft.Extensions.DependencyInjection;

public static class WebhookClients
{
    public static IServiceCollection AddWebhookClients(this IServiceCollection services)
    {
        // An organization's webhook: guarded everywhere.
        services.AddHttpClient("webhooks").AddSsrfProtection();

        // An organization's identity provider: guarded in the cloud only.
        services.AddHttpClient("identity-provider").AddCloudSsrfProtection();

        return services;
    }
}
```

A request to a private, loopback, link-local or other reserved address, or to a name that can't be resolved, throws
`SsrfProtectionException` before anything connects. See the
[README](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Http/src/README.md) for
a complete program.

## Documentation

### SSRF protection

- The guard resolves each request's host, refuses it if any address it resolves to is internal or if it can't be
  resolved, and connects to the address it checked, keeping the original host for TLS. The request names the host
  again once the response arrives.
- It turns off automatic redirects on the client's primary handler, whoever sets it, follows up to 10 redirects
  itself and checks every hop. A redirect from `https://` to `http://` is returned, not followed.
  `AddSsrfProtection(followRedirects: false)` returns redirects to a client that follows them itself. A client
  guarded twice gets one guard, with the first registration's `followRedirects`.
- `AddCloudSsrfProtection()` leaves a self-hosted install's client unguarded, for destinations its operator chose.
  It needs `IBitwardenEnvironment`, which `AddBitwardenEnvironment()` registers.
- `IPAddress.IsInternal()` is the guard's check, for refusing an address when it is saved.

### Internal TLS

- `InternalTls.LoadAuthorities()` reads the named PEM files. `InternalTls.Trusts(authorities, certificate, chain,
errors)` trusts a server's certificate when the system does, or when it chains to a named root, is valid now and is
  allowed for server authentication. A host name that doesn't match is never trusted. It checks revocation and
  downloads certificates only as the system's chain was set to.
- `InternalTls.LoadServerCertificate()` reads a certificate and key given as settings, and throws, naming the
  settings and never a value, when they can't be used.

### Types

| Type                              | Purpose                                                                 |
| --------------------------------- | ----------------------------------------------------------------------- |
| `HttpClientBuilderSsrfExtensions` | `AddSsrfProtection()` and `AddCloudSsrfProtection()` on an HTTP client  |
| `SsrfProtectionException`         | A request the guard refused                                             |
| `IPAddressExtensions`             | `IsInternal()`: whether an address is in a private or reserved range    |
| `InternalTls`                     | The `Tls` settings, the authorities trusted, and the server certificate |
