# Bitwarden.Server.Sdk.Crypto

Bitwarden SDK crypto for the server, backed by the Rust SDK (`bitwarden-uniffi-server` in
sdk-internal). Ships native libraries for `linux-x64`, `linux-arm64`, `osx-arm64` and `win-x64`.

```csharp
services.AddServerSdkCrypto();

// Which keys does a crypto object name? Strict parse, nothing decrypted or verified.
EncStringInfo info = parsing.InspectEncString(encString);        // algorithm, key id
CryptoObjectKeyIds ids = parsing.InspectSymmetricKeyEnvelope(envelope);
// ids.ContainedKeyId, ids.EncryptedByKeyId, ids.SignedByKeyId
```

`ICryptoParsingClient` covers EncStrings, UnsignedSharedKeys, SymmetricKeyEnvelopes,
LegacyCompatSymmetricKeyEnvelopes, PasswordProtectedKeyEnvelopes, DataEnvelopes, SignedObjects,
VerifyingKeys and PublicKeys.

Invalid input throws `InvalidEncryptedDataException`.
