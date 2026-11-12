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

Store the hash of what the client sent:

```csharp
user.MasterPassword = hasher.CreateStoredHash(request.MasterPasswordHash);
```

Check it, and replace a hash stored with older parameters:

```csharp
var result = hasher.VerifyStoredHash(user.MasterPassword, request.MasterPasswordHash);
if (result == PasswordHashVerification.SuccessRehashNeeded)
{
    user.MasterPassword = hasher.CreateStoredHash(request.MasterPasswordHash);
}

return result != PasswordHashVerification.Failed;
```

## Algorithm

ASP.NET Core Identity's V3 format: PBKDF2-HMAC-SHA512, 100,000 iterations, 128-bit random salt,
256-bit subkey. A host's own `PasswordHasherOptions` do not change it.
