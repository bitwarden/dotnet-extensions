# Bitwarden.Server.Sdk.Crypto

Bitwarden SDK crypto for the server, backed by the Rust SDK (`bitwarden-uniffi-server` in
sdk-internal). Ships native libraries for `linux-x64`, `linux-arm64`, `osx-arm64` and `win-x64`.

```csharp
services.AddServerSdkCrypto();

// Which key encrypted this data? Hex key id, or null for legacy EncStrings.
string? keyId = keyIdReader.FromEncString(encString);
string? sealedKeyId = keyIdReader.FromSymmetricKeyEnvelope(envelope);
```

Invalid input throws `InvalidEncryptedDataException`.
