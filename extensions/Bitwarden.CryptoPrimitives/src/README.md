# Bitwarden.CryptoPrimitives

**Audience:** engineers changing this package.

Cryptographic primitives shared by Bitwarden services. See [PACKAGE.md](PACKAGE.md) for how to use it.

## What belongs here

- **Only `System.Security.Cryptography`.** No third-party crypto, so every primitive runs in the
  platform's validated module (CNG on Windows, OpenSSL on Linux).
- **Stable encodings.** An output is persisted or shared between services. Changing how bytes are
  encoded changes every output: add a new primitive instead.

## Testing

Every primitive has known-answer vectors computed by an independent implementation. Run them from the
repository root:

```bash
dotnet test --project extensions/Bitwarden.CryptoPrimitives/tests/Bitwarden.CryptoPrimitives.Tests
```
