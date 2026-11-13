# Bitwarden.Server.Sdk.Http

**Audience:** engineers changing this package.

SSRF protection and internal TLS trust for a Bitwarden service's outbound HTTP calls. See [PACKAGE.md](PACKAGE.md) for
how to use it.

## What belongs here

What makes an outbound `HttpClient` call safe, for any host: a web service, a worker or a client library. Nothing that
depends on ASP.NET Core. Applying the `Tls` settings to a host's listeners and to every client it makes belongs to the
host, not here.

## How it works

`AddSsrfProtection()` and `AddCloudSsrfProtection()` register three things on a named client:

- `SsrfProtectionHandler`, an additional delegating handler. It skips adding itself when the client already has one: a
  second guard below the first would follow a redirect itself, and the first would refuse its answer.
- `SsrfConnector`, a singleton: the network as the guard sees it. It resolves names, and opens each connection to the
  address the guard checked for the request that asked for it (an `SsrfPin` in the request's options). It refuses a
  connection to any other host, unless the request goes through a proxy. Tests replace it to fake DNS and sockets.
- `SsrfPrimaryHandlerFilter`, an `IHttpMessageHandlerBuilderFilter`. A configuration action reaches only the primary
  handler set when it runs, so a filter, which runs after every configuration action, does the setting up. It moves the
  guard to the end of the additional handlers, directly above the primary handler, so the client's own handlers never
  see a redirect hop. It replaces the factory's own `HttpClientHandler`, one with nothing set, with a
  `SocketsHttpHandler`, follows a `DelegatingHandler` chain to the handler that sends, and requires a
  `SocketsHttpHandler` there. It turns that handler's redirects and cookies off, sets `SsrfConnector.ConnectAsync` as
  its `ConnectCallback` and gives it to the guard as its `Sender`. Anything else fails the client's creation. A client
  whose handlers were cleared after the guard was added has no guard, and is left alone.

The guard refuses to send unless its `Sender` is below it and still set up that way, so a filter that runs later and
swaps or changes the primary handler can't leave it unpinned. It refuses a request that may use HTTP/3, whose QUIC
connections bypass `ConnectCallback`.

For each hop, `SsrfProtectionHandler.CheckAsync` resolves the host (its `IdnHost` form for a name), refuses when any
address is internal or none resolves, and picks the first IPv4 address (else the first address). `SendCheckedAsync`
sets the pin and sends. A request that goes through a proxy (the sender's own, else `HttpClient.DefaultProxy`, unless
it bypasses the host) is addressed to the checked address instead, with the host in the `Host` header, so the proxy
can't resolve the name differently; the host is put back on the request and the response's request once the response
arrives. After sending, a response whose request was readdressed below the guard is refused; the request has gone out
by then. When it follows redirects it checks the first hop before buffering the body, keeps the body's bytes and
content headers, and builds each hop's request fresh from them, which is why no original header reaches the next hop.
The method changes on a redirect as `SocketsHttpHandler` changes it.

`IPAddressExtensions.IsInternal()` holds the reserved ranges, from the IANA IPv4 and IPv6 Special-Purpose Address
Registries plus Azure's platform address `168.63.129.16` and deprecated IPv6 ranges. An IPv6 address that embeds an
IPv4 one (IPv4-mapped, NAT64 `64:ff9b::/96`, 6to4 `2002::/16`) is internal when the embedded address is, since a
gateway on the path would translate it back. Each range carries its RFC in a comment.

`InternalTls.LoadServerCertificate()` exports the certificate with its key to PKCS #12 and reads it back, because an
ephemeral key, as `CopyWithPrivateKey` makes, isn't usable by `SslStream` on Windows. It zeroes the key bytes and the
export after.
`InternalTls.Trusts` builds a second chain with custom root trust, reusing the system chain's certificates, revocation
and download settings.

A change must keep:

- **Check before connecting.** Every address a name resolves to is checked, and the request connects to the one that
  was, so a name can't resolve elsewhere in between, directly or through a proxy.
- **Every hop checked.** Automatic redirects stay off below the guard, whoever configures the primary handler, and no
  redirect leaves TLS.
- **Nothing between the guard and the sender but what the factory adds.** The client's own handlers run above it.
- **No secret in a message.** A refusal names the setting, never a key, a certificate or a path's contents.

## Testing

Every source file has a matching test class in `tests/Bitwarden.Server.Sdk.Http.Tests`.

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Http/tests/Bitwarden.Server.Sdk.Http.Tests
```
