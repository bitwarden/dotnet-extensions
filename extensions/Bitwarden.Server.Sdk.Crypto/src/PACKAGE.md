# Bitwarden.Server.Sdk.Crypto

Bitwarden SDK crypto for the server, backed by the Rust SDK (`bitwarden-uniffi-server` in
sdk-internal). Ships native libraries for `linux-x64`, `linux-arm64`, `osx-arm64` and `win-x64`.

```csharp
services.AddServerSdkCrypto();

// Which keys does a crypto object name? Strict parse, nothing decrypted or verified.
EncStringInfo info = parsing.InspectEncString(encString);        // algorithm, key id
CryptoObjectKeyIds ids = parsing.InspectSymmetricKeyEnvelope(envelope);
// ids.ContainedKeyId, ids.EncryptedByKeyId, ids.SignedByKeyId

// Is this key service data well formed, and which keys does it name?
KeyInfo key = keyData.ValidateKey(data);                         // type, key ids
UnlockMethodInfo unlock = keyData.ValidateUnlockMethod(data);    // kind, key ids
```

`ICryptoParsingClient` covers EncStrings, UnsignedSharedKeys, SymmetricKeyEnvelopes,
LegacyCompatSymmetricKeyEnvelopes, PasswordProtectedKeyEnvelopes, DataEnvelopes, SignedObjects,
VerifyingKeys and PublicKeys.

`IKeyDataClient` validates the key service's key and unlock method `data`: its shape, every crypto object in it, ids
its objects agree on, and a master password's KDF.

Invalid input throws `InvalidEncryptedDataException`; refused key data throws `InvalidKeyDataException`, whose `Error`
says why.
