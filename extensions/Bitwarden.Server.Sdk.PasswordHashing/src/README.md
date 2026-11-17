# Bitwarden.Server.Sdk.PasswordHashing

Server-side hashing of client-derived password hashes. See [PACKAGE.md](PACKAGE.md) for usage.
Owned by the Key Management team.

## Compatibility

Hashes stored by the Bitwarden server must keep verifying. The package therefore wraps ASP.NET
Identity's `PasswordHasher` with the server's parameters, rather than reimplementing it. The tests
pin the format independently of Identity, so a change to either side fails them.

## Testing

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.PasswordHashing/tests/Bitwarden.Server.Sdk.PasswordHashing.Tests
```
