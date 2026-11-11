namespace Bitwarden.Server.Sdk.Crypto.Tests;

/// <summary>
/// Test vectors pinned in sdk-internal (paths relative to <c>crates/</c>), with the key ids their
/// headers name.
/// </summary>
internal static class InspectorFixtures
{
    // bitwarden-core/src/key_management/webauthn_prf.rs: ENCRYPTED_USER_KEY, RSA-2048-OAEP-SHA1.
    public const string UnsignedSharedKey =
        "4.DMD1D5r6BsDDd7C/FE1eZbMCKrmryvAsCKj6+bO54gJNUxisOI7SDcpPLRXf+JdhqY15pT+wimQ5cD9C+6OQ6s71LFQHewXPU29l9Pa1JxGeiKqp37KLYf+1IS6UB2K3ANN35C52ZUHh2TlzIS5RuntxnpCw7APbcfpcnmIdLPJBtuj/xbFd6eBwnI3GSe5qdS6/Ixdd0dgsZcpz3gHJBKmIlSo0YN60SweDq3kTJwox9xSqdCueIDg5U4khc7RhjYx8b33HXaNJj3DwgIH8iLj+lqpDekogr630OhHG3XRpvl4QzYO45bmHb8wAh67Dj70nsZcVg6bAEFHdSFohww==";

    // Sealing key of Fixtures.Envelope, which seals Fixtures.KeyId.
    public const string EnvelopeSealingKeyId = "f145204af724a7dcf111dc72fbdf504c";

    // bitwarden-crypto/src/compat/legacy_compat_symmetric_key_envelope.rs:
    // TEST_VECTOR_ENVELOPE_XAES256_GCM_JSON, the envelope form.
    public const string LegacyCompatEnvelope =
        "g1hgpgE6AAEReQN4ImFwcGxpY2F0aW9uL3guYml0d2FyZGVuLmxlZ2FjeS1rZXkEUM8BFpaam4mj7QFIQf7RnyY6AAEVXFBOFUXyYMu3MZtMG0+1s6weOgABOIEDOgABOIAioQVYGC3aRnWqCU2AKkSnIhnvuw0BL4U239v5iVhQ6RpmuYoqvK660Q17JeCRvywXvfM0fQ6OccdJBBQr/RxE5D4N4rwp2T1tPWLRP0K40wfL/pNb4nKdPVY4GBe4dSlT5RN+UKabC7oe8YbpUVU=";
    public const string LegacyCompatEnvelopeSealedKeyId = "4e1545f260cbb7319b4c1b4fb5b3ac1e";
    public const string LegacyCompatEnvelopeSealingKeyId = "cf0116969a9b89a3ed014841fed19f26";

    // Same file: TEST_VECTOR_ENCSTRING_XAES256_GCM_JSON, the COSE EncString form.
    public const string LegacyCompatEncString =
        "7.g1g+owE6AAEReQN4ImFwcGxpY2F0aW9uL3guYml0d2FyZGVuLmxlZ2FjeS1rZXkEUP5m8qzT6Q8wxOngKUeTNDChBVgYL4b8GbJ/8WekzkDiaijkGjFrpCco13sAWFDd+iZoFmFx6kspDvvpTpgtyyJ2z0pxISAEK7rnhK3YWoIiQ7O7kPgrM/tAU2juD2mUzdhbCF5kO7wQwhd54QJ91i2B3ySFccRAugZngAjKag==";
    public const string LegacyCompatEncStringSealingKeyId = "fe66f2acd3e90f30c4e9e02947933430";

    // bitwarden-crypto/src/safe/password_protected_key_envelope.rs: TESTVECTOR_COSEKEY_ENVELOPE,
    // base64 encoded.
    public const string PasswordProtectedEnvelope =
        "hEShAxhloQVYGAEfOuYKXMPp1Aem/ENz3ToDv9q8tcAcC1hUjbeJp6ahIVIe/xcKs5VYGCc8SuiFLFpidR8pRftM+o3lU78G7Wt/Xe5uMX3JJaJ4nSB0w9CPU/7fXWGeABhfxfkj8AMUR6RhtB3LRR+X+fTFW2WugYNHoQE6AAEVV6UBOgABFVc6AAEVWQM6AAEVWhoAAQAAOgABFVsEOgABFVhQpf048/829vzn5iH8Ma8Bb/Y=";

    // bitwarden-crypto/src/safe/data_envelope.rs: TEST_VECTOR_AES_GCM_ENVELOPE and its CEK's id.
    public const string DataEnvelope =
        "g1hHpQEDA3gjYXBwbGljYXRpb24veC5iaXR3YXJkZW4uY2Jvci1wYWRkZWQEUMsjH6LNIuT2H2xWMcXOkqg6AAE4gQI6AAE4gCChBUyoF/oGEm+lJYrjjgdYUGIH5LnQjqFMWo2BJORVPYH2+hEWkxIn3tRgAMHNwIr0nTXMVD1EyVGZOsHSDMPqn2HaYrDeR5s+Rg0ezZ2WLh8n2FbdC44A/ExOms4IHcyT";
    public const string DataEnvelopeKeyId = "cb231fa2cd22e4f61f6c5631c5ce92a8";

    // bitwarden-crypto/src/signing/signed_object.rs: SIGNED_OBJECT and its VERIFYING_KEY, base64
    // encoded.
    public const string SignedObject =
        "hFgepAEnAxg8BFA3gyi/5olMtriLXpgtPw1HOgABOH8goFWhZmZpZWxkMWxUZXN0IG1lc3NhZ2VYQM5TsbglZ4AneK49BB24RC4vyy/2bKCpcgeld8YD0TT5WR+c/9RL4E63Ja4/cEbb9hPVEXn59Be2JMGvN/pB+gY=";
    public const string VerifyingKey =
        "pgEBAlA3gyi/5olMtriLXpgtPw1HAycEgQIgBiFYIF3VI7FR2+Lxk4zuICK31WvjXEtU0C/GUBK8rJG4mhqq";
    public const string VerifyingKeyId = "378328bfe6894cb6b88b5e982d3f0d47";

    // bitwarden-crypto/src/keys/public_key_encryption.rs: RSA_PUBLIC_KEY_B64, and its key id from
    // test_public_key_algorithm_and_key_id.
    public const string PublicKey =
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEArvcXfr5pCD6KhzXo7BWc5Hdcbgp9U6hk0+wDYQBJ2yP8mlbd3GiN9JMFAtliE6BaTYLuxI9Mdk7XmDoKy63XAuI8tUon5imL/792Wca3f3qrbZh9pOfPKWp7HkcByty1ZO8QPlEYUP24y4DzOfVdLkdZfs9X5qKHiTxc+VklzTm3PSap4eORTQ/lP1GB10y0qJk5+44GRcSQSr3ku6ui2re8AJ2GQhdnZz5oWaCb/kij5bQPBwBrIEBlgRdaeasVdR6wFJPJAQZxtqWo9MPKeVDOkaQ3Qrryh+49S4rln3592/WeHYM5hO47DJr86ELcqcyCmksYas7xTqHfVfHSXQIDAQAB";
    public const string PublicKeyId = "04fbcfa50c5805171304cc5b4794c25d";
}
