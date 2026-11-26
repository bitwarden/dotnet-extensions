# Bitwarden.Server.Sdk.Http

**Audience:** engineers changing this package.

SSRF protection and internal TLS trust for a Bitwarden service's outbound HTTP calls. See [PACKAGE.md](PACKAGE.md) for
how to use it.

## What belongs here

What makes an outbound `HttpClient` call safe, for any host: a web service, a worker or a client library. Nothing that
depends on ASP.NET Core. Serving TLS (a host's certificate, its listeners) belongs to the host, not here.

## How it works

`AddSsrfProtection()` and `AddCloudSsrfProtection()` register three things on a named client:

- `SsrfProtectionHandler`, an additional delegating handler. It skips adding itself when the client already has one: a
  second guard below the first would follow a redirect itself, and the first would refuse its answer.
- `SsrfResolver`, a singleton: the system's DNS, which tests replace to fake it.
- `SsrfPrimaryHandlerFilter`, an `IHttpMessageHandlerBuilderFilter`. A configuration action reaches only the primary
  handler set when it runs, so a filter, which runs after every configuration action, does the setting up. It moves the
  guard to the end of the additional handlers, so the client's own handlers never see a redirect hop; only a handler a
  filter registered earlier adds afterwards (the factory's logging handler) sits below it. It requires the primary
  handler to be a `SocketsHttpHandler`, not a `DelegatingHandler` wrapping one, which would see every hop. It turns that
  handler's redirects and cookies off (cookies would be kept by the address each request is sent to, which many hosts
  can share) and gives it to the guard as its `Sender`. Anything else fails the client's creation. A client whose
  handlers were cleared after the guard was added has no guard, and is left alone.

The guard refuses to send unless its `Sender` is below it and still set up that way, so a filter that runs later and
swaps or changes the primary handler can't turn redirects or cookies back on. It refuses a request that may use HTTP/3:
a server's `Alt-Svc` header can name another host, which `SocketsHttpHandler` reaches over QUIC by resolving the name
itself.

For each hop, `SsrfProtectionHandler.CheckAsync` resolves the host (its `IdnHost` form for a name), refuses when any
address is internal or none resolves, and picks the first IPv4 address (else the first address). `SendCheckedAsync`
addresses the request to the checked address, with the host in the `Host` header (unless the caller set one), so
neither the sender nor a proxy can resolve the name differently; the host is put back on the request and the
response's request once the response arrives. `SocketsHttpHandler` takes the name TLS asks for and checks the
certificate against from the `Host` header; `SocketsHttpHandlerTests` pins that down, so a runtime that changes it
fails there. After sending, a response whose request was readdressed below the guard is refused; the request has gone out
by then. When it follows redirects it checks the first hop before buffering the body, keeps the body's bytes and
content headers, and builds each hop's request fresh from them, which is why no original header reaches the next hop.
The method changes on a redirect as `SocketsHttpHandler` changes it.

`IPAddressExtensions.IsInternal()` holds the reserved ranges, from the IANA IPv4 and IPv6 Special-Purpose Address
Registries plus Azure's platform address `168.63.129.16` and deprecated IPv6 ranges. An IPv6 address that embeds an
IPv4 one (IPv4-mapped, NAT64 `64:ff9b::/96`, 6to4 `2002::/16`) is internal when the embedded address is, since a
gateway on the path would translate it back. Each range carries its RFC in a comment.

`InternalTls.ChainPolicy` is an `X509ChainPolicy` with custom root trust, given to `SslStream` through
`SslClientAuthenticationOptions.CertificateChainPolicy`, with no validation callback: `SslStream` checks the name and
adds the server-authentication usage itself. It checks no revocation and downloads nothing.

A change must keep:

- **Check before sending.** Every address a name resolves to is checked, and the request is addressed to the one that
  was, so a name can't resolve elsewhere in between, directly or through a proxy.
- **Every hop checked.** Automatic redirects stay off below the guard, whoever configures the primary handler, and no
  redirect leaves TLS.
- **Nothing the client adds between the guard and the sender.** The client's own handlers run above it.
- **No secret in a message.** A refusal names the setting, never a certificate or a path's contents.
- **No hand-built TLS validation.** Trust is a chain policy `SslStream` applies, not a callback of ours.

## Testing

Every source file with logic has a matching test class in `tests/Bitwarden.Server.Sdk.Http.Tests`.

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Http/tests/Bitwarden.Server.Sdk.Http.Tests
```
