# Bitwarden.Server.Sdk.Common

## About

The types still shared here: the attributes that mark a field as Protected Data or as personal data. The error document, its codes and the standard exceptions are in
Bitwarden.Server.Sdk.Errors, and the context headers, values and tokens are in
Bitwarden.Server.Sdk.Context.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Common
```

## Usage

### Mark a field that holds encrypted data

Bitwarden servers only store encrypted data and never decrypt it. An encrypted string is longer
than a typical default string limit, so declare the field and its maximum length:

```csharp
[Encrypted(MaxLength = 10_000)]
public required string Notes { get; init; }
```

Only the length is checked, unless you ask for the shape too. `WellFormed = true` also refuses a value
that isn't shaped like an encrypted string (its type, pieces and base64), as "Notes is not a valid
encrypted string.":

```csharp
[Encrypted(MaxLength = 10_000, WellFormed = true)]
public required string Notes { get; init; }
```

A value that is too long as well is refused as malformed, and so is a value that isn't a string. The
value is never decrypted. `EncryptedStringFormat.IsWellFormed` makes the same check in code.

## Types

| Type                                          | Purpose                                            |
| --------------------------------------------- | -------------------------------------------------- |
| `EncryptedAttribute`, `PersonalDataAttribute` | Mark a field as Protected Data or as personal data |
