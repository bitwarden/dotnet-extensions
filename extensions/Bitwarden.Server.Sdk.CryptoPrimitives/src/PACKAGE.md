# Bitwarden.Server.Sdk.CryptoPrimitives

## About

Cryptographic primitives built only on `System.Security.Cryptography`.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.CryptoPrimitives
```

## Usage

### Derive a cache key

Derive a 32-byte cache key from a secret and a list of strings:

```csharp
var secret = CacheSecret.FromString(configuration["Caching:Secret"]!);  // base64, 32 bytes
var key = CacheKey.DeriveCacheKey(secret, "account-by-email", email);
var redisKey = key.ToString();  // base64
```

The key is `HMAC-SHA-256(secret, LP(p0) || ... || LP(pn))`, where
`LP(x) = uint64_be(len(utf8(x))) || utf8(x)`. The length prefixes keep `("ab", "c")` and `("a", "bc")`
apart.

`CacheSecret.Development` is 32 zero bytes, the same as `default`. Use it only in development:
anyone can derive the same keys from it.

## Gotchas and constraints

- A parameter that is not valid UTF-16, such as a lone surrogate, throws `EncoderFallbackException`
  rather than being replaced and colliding with another string.
- `ToString()` returns base64, and `FromString` reads it back, for both types. `ToString()` of a
  `CacheSecret` writes the secret itself: keep it out of logs.

## Types

| Type          | Purpose                                         |
| ------------- | ----------------------------------------------- |
| `CacheSecret` | The 32-byte secret cache keys are derived from  |
| `CacheKey`    | A 32-byte cache key derived from a secret       |
