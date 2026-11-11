# Bitwarden.Server.Sdk.Crypto

Bitwarden SDK crypto for the server, backed by the Rust SDK (`bitwarden-uniffi-server` in
sdk-internal). Ships native libraries for `linux-x64`, `linux-arm64`, `osx-arm64` and `win-x64`.

```csharp
services.AddServerSdkCrypto();

// Which key encrypted this data? Hex key id, or null for legacy EncStrings.
string? keyId = keyIdReader.FromEncString(encString);
string? sealingKeyId = keyIdReader.FromSymmetricKeyEnvelope(envelope);

// Which keys does a crypto object name? Strict parse, nothing decrypted or verified.
EncStringInfo info = inspector.InspectEncString(encString);    // algorithm, key id
CryptoObjectKeyIds ids = inspector.InspectSymmetricKeyEnvelope(envelope);
// ids.ContainedKeyId, ids.EncryptedByKeyId, ids.SignedByKeyId
```

`ICryptoObjectInspector` covers EncStrings, UnsignedSharedKeys, SymmetricKeyEnvelopes,
LegacyCompatSymmetricKeyEnvelopes, PasswordProtectedKeyEnvelopes, DataEnvelopes, SignedObjects,
VerifyingKeys and PublicKeys.

Invalid input throws `InvalidEncryptedDataException`.
