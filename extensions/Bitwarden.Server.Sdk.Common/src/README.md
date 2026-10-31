# Bitwarden.Server.Sdk.Common

Types that a Bitwarden service and the code that calls it both need to agree on. See
[PACKAGE.md](PACKAGE.md) for how to use them.

## What belongs here

Only what both a service and a caller of that service need. The test is whether removing a type
would force a caller to reference the package that hosts the service.

Things that look shared but are not:

- Resource declarations a service authors. A client consumes generated types for them instead.
- Anything touching `HttpContext`, MVC or `HttpClient`.

## Why it exists

The hosting package must be free to change without considering every caller. That is only true if
callers do not reference it, which is only true if the types they share live somewhere neither
owns.

## Configuration

The package reads no configuration itself. It names the settings that the hosting and client
packages read, so a service and its callers agree on them:

| Setting                             | Read by           | Effect                                                                                                                       |
| ----------------------------------- | ----------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `Tls:CertificateAuthorities`        | `InternalTls`     | PEM files of root authorities trusted for calls between services, besides the system's. Unset: only the system's are trusted |
| `Tls:RequireHttps`                  | `InternalTls`     | When true, a host refuses a plain `http://` address for another service. Unset: plain addresses are allowed                  |
| `Caching:Owned:Redis:Configuration` | `OwnedCacheEntry` | The service cache's connection. Unset: there is no service cache, and clients always ask the service                         |
| `Caching:Owned:AnswerSecret`        | `OwnedAnswerKey`  | Base64, at least 32 bytes. Keys the digest of answers keyed by text with HMAC-SHA-256. Unset: plain SHA-256                  |

### Choosing whether to key answer digests

An answer keyed by text, such as an email address, is stored under a digest of that text, never
the text itself. With no secret the digest is plain SHA-256, which anyone can compute. Someone who
can read the cache and guess an address can then confirm it is there. That is acceptable only
where the cache is as well protected as the database behind it.

Set `Caching:Owned:AnswerSecret` to make the digest an HMAC that only holders of the secret can
compute. Treat it like any other secret:

- **Every service and client sharing the cache must hold the same secret.** One holding a different
  secret, or none, computes subjects that no owner fills. It never reads a wrong answer: it always
  misses and asks the owner, so a mismatch costs speed, not correctness.
- **Changing the secret moves every answer keyed by text.** The cache misses until owners fill the
  new subjects, and entries under the old ones expire on their own.
- **A secret that is not base64 or is shorter than 32 bytes** stops the service at startup, without
  echoing the value.

Answers keyed by an identifier are stored under the identifier itself and are not affected.

## Testing

Every source file has a matching test class in
`tests/Bitwarden.Server.Sdk.Common.Tests`, in a folder that mirrors `src`.

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Common/tests/Bitwarden.Server.Sdk.Common.Tests
```
