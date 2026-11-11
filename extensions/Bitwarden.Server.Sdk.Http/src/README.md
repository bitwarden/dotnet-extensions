# Bitwarden.Server.Sdk.Http

SSRF protection and internal TLS trust for a Bitwarden service's outbound HTTP calls. See [PACKAGE.md](PACKAGE.md) for
how to use it.

## What belongs here

What makes an outbound `HttpClient` call safe, for any host: a web service, a worker or a client library. Nothing that
depends on ASP.NET Core. Applying the `Tls` settings to a host's listeners and to every client it makes belongs to the
host, not here.

## How it works

`AddSsrfProtection()` and `AddCloudSsrfProtection()` register two things on a named client:

- `SsrfProtectionHandler`, an additional delegating handler. It skips adding itself when the client already has one: a
  second guard below the first would follow a redirect itself, and the first would refuse its answer.
- `SsrfPrimaryHandlerFilter`, an `IHttpMessageHandlerBuilderFilter`. A configuration action reaches only the primary
  handler set when it runs, so a primary handler named after the guard would keep following redirects. The filter runs
  after every configuration action, follows a `DelegatingHandler` chain to the handler that sends, and turns
  `AllowAutoRedirect` off on a `SocketsHttpHandler` or `HttpClientHandler`. Any other primary handler has no switch;
  the handler then refuses an answer whose request URI isn't the address it sent to.

For each hop, `SsrfProtectionHandler.ValidateAndSendAsync` resolves the host (its `IdnHost` form for a name), refuses
when any address is internal or none resolves, rewrites the request URI to the first IPv4 address (else the first
address) with the original host in the `Host` header, sends, and puts the host back on the request and the response's
request. When it follows redirects it keeps the body's bytes and content headers, and builds each hop's request fresh
from them, which is why no original header reaches the next hop. The method changes on a redirect as
`SocketsHttpHandler` changes it.

`IPAddressExtensions.IsInternal()` holds the reserved ranges, from the IANA IPv4 and IPv6 Special-Purpose Address
Registries plus Azure's platform address `168.63.129.16` and deprecated IPv6 ranges. An IPv6 address that embeds an
IPv4 one (IPv4-mapped, NAT64 `64:ff9b::/96`, 6to4 `2002::/16`) is internal when the embedded address is, since a
gateway on the path would translate it back. Each range carries its RFC in a comment.

`InternalTls.LoadServerCertificate()` exports the certificate with its key to PKCS #12 and reads it back, because an
ephemeral key, as `CopyWithPrivateKey` makes, isn't usable by `SslStream` on Windows. It zeroes the key bytes after.
`InternalTls.Trusts` builds a second chain with custom root trust, reusing the system chain's certificates, revocation
and download settings.

A change must keep:

- **Check before connecting.** Every address a name resolves to is checked, and the request connects to the one that
  was, so a name can't resolve elsewhere in between.
- **Every hop checked.** Automatic redirects stay off below the guard, whoever configures the primary handler, and no
  redirect leaves TLS.
- **No secret in a message.** A refusal names the setting, never a key, a certificate or a path's contents.

## Testing

Every source file has a matching test class in `tests/Bitwarden.Server.Sdk.Http.Tests`.

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Http/tests/Bitwarden.Server.Sdk.Http.Tests
```
