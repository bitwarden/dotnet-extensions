# Bitwarden.Server.Sdk.PasswordHashing

## About

Hashes a password hash a client derived (a master password hash or a Send password hash) before a
service stores it, and checks a client's hash against the stored one. Hashes are compatible with
those the Bitwarden server stores: either side verifies the other's.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.PasswordHashing
```

## Usage

```csharp
services.AddServerPasswordHasher();
```

Wrap what the client sent in `ClientCreatedHash` and store the `ServerStorableHash` it returns. The
wrappers keep the two from being swapped, and their `ToString` hides the value:

```csharp
var clientHash = new ClientCreatedHash(request.MasterPasswordHash);
user.MasterPassword = hasher.CreateStoredHash(clientHash).Value;
```

Check it, and replace a hash stored with older parameters:

```csharp
var clientHash = new ClientCreatedHash(request.MasterPasswordHash);
var result = hasher.VerifyStoredHash(new ServerStorableHash(user.MasterPassword), clientHash);
if (result == PasswordHashVerification.SuccessRehashNeeded)
{
    user.MasterPassword = hasher.CreateStoredHash(clientHash).Value;
}

return result != PasswordHashVerification.Failed;
```

## Algorithm

ASP.NET Core Identity's V3 format: PBKDF2-HMAC-SHA512, 100,000 iterations, 128-bit random salt,
256-bit subkey. A host's own `PasswordHasherOptions` do not change it.
