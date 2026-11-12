namespace Bitwarden.Server.Sdk.Crypto.Tests;

/// <summary>
/// Key service <c>data</c> built with the SDK in bitwarden-uniffi-server's key_data tests, with the key ids its
/// objects carry.
/// </summary>
internal static class KeyDataFixtures
{
    // The RSA public key's id, and the Ed25519 verifying key's, which signs SignedKey and SecurityState.
    public const string PublicKeyId = "7b0cb10634eab37ece84f915851abcf6";
    public const string VerifyingKeyId = "076f9e0a98066cb6d4273fe38796e94c";

    // Envelope holds SealedKeyId, sealed by SealingKeyId; the EncString wrapped keys are sealed by SealingKeyId too.
    public const string SealedKeyId = "4ac8a409e4f32b95181cc2ad5238878f";
    public const string SealingKeyId = "6968e10eb9802297090562b0223de880";
    public const string Envelope = "g1g+pgE6AAEReQMYZQRQaWjhDrmAIpcJBWKwIj3ogDoAARVcUErIpAnk8yuVGBzCrVI4h486AAE4gQM6AAE4gAKhBVgYA67MRRZiVJqwDRCDdRrGMm5o/lDTLRMFWFRCz3VApxYo+BUoOlEZa0pqZV34F14MjdgYDU+OQ+gmM8pLOPiaGAHHZ9Iyl1DKThsxkQF1za+BXbE047lDbcHYCwfmw7hIgXagQIGUo3XGU60yj98=";

    // A private key with signedKey; its wrappedKey is an EncString, naming no contained key.
    public const string PrivateKey =
        """
        {"publicKey":"MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAoUsc+1bbhl5/ToyWGtOmcB3NyhMzcy6vJuvzhrAw8k8qoVF1cFvq7ecC/iBPm+2+MdSczpnqJh/7T0DVeOrCaxBhKaKrFr6eJCR9p/5pKUcBdR4C9dtcbulhNBRadW36hGAA3aZgO52H8LRnQZBxHaKe2TnV+1gmL74bTjBuJ0tAmz1KxddexOyrMXK0R1q5dvqXzXDw1QxPx3v0vIDYLZn9StmiyVZ64dV424QDrVsxk2B6VeFI0t+Dv/r1c/0Ru/2RRtvwC4lHkSg8LGWY9aOUk8kiWzomF6nfTLSioBM7oOa2WLVR4Hb0zhsi+wf33m7GrtSCYtv2CkiXvfWLGwIDAQAB","signedKey":"hFgepAEnAxg8BFAHb54KmAZsttQnP+OHlulMOgABOH8BoFkBTqNpYWxnb3JpdGhtAG1jb250ZW50Rm9ybWF0AGlwdWJsaWNLZXlZASYwggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIBAQChSxz7VtuGXn9OjJYa06ZwHc3KEzNzLq8m6/OGsDDyTyqhUXVwW+rt5wL+IE+b7b4x1JzOmeomH/tPQNV46sJrEGEpoqsWvp4kJH2n/mkpRwF1HgL121xu6WE0FFp1bfqEYADdpmA7nYfwtGdBkHEdop7ZOdX7WCYvvhtOMG4nS0CbPUrF117E7KsxcrRHWrl2+pfNcPDVDE/He/S8gNgtmf1K2aLJVnrh1XjbhAOtWzGTYHpV4UjS34O/+vVz/RG7/ZFG2/ALiUeRKDwsZZj1o5STySJbOiYXqd9MtKKgEzug5rZYtVHgdvTOGyL7B/febsau1IJi2/YKSJe99YsbAgMBAAFYQJ2wKPrixdU4m5x/ntbDc+Jl8SmmaZxtdWizipcUbSlb5y8BnBu5bHscaV2jbZLeyn8BL7mqHQHxZCxYNUKk9QE=","type":"legacy-compat-private-key-envelope","wrappedKey":"7.g1gcowE6AAEReQMYZQRQaWjhDrmAIpcJBWKwIj3ogKEFWBibXEwdYzQKzeR1g34Khp+MNvKqUIhimypYVBpSgXh14slMH7gN0AgEJ+BF1APT4DUoWkE87m3kmFoSKyjkQguYdoH8idmvwWKBEdLFCFZ/AGM6SheLPIxrowven1YH+g7TZl5Q+Xza1/sjquC9bw=="}
        """;

    // A signing key; its wrappedKey is an EncString.
    public const string SigningKey =
        """
        {"publicKey":"pgEBAlAHb54KmAZsttQnP+OHlulMAycEgQIgBiFYILhg49i9FEtlTU/OU3K1QPHcPmuNaiMlDxuCYOUG4WAy","type":"legacy-compat-signing-key-envelope","wrappedKey":"7.g1gcowE6AAEReQMYZQRQaWjhDrmAIpcJBWKwIj3ogKEFWBibXEwdYzQKzeR1g34Khp+MNvKqUIhimypYVBpSgXh14slMH7gN0AgEJ+BF1APT4DUoWkE87m3kmFoSKyjkQguYdoH8idmvwWKBEdLFCFZ/AGM6SheLPIxrowven1YH+g7TZl5Q+Xza1/sjquC9bw=="}
        """;

    // A symmetric key in Envelope.
    public const string SymmetricKey =
        """
        {"type":"legacy-compat-symmetric-key-envelope","wrappedKey":"g1g+pgE6AAEReQMYZQRQaWjhDrmAIpcJBWKwIj3ogDoAARVcUErIpAnk8yuVGBzCrVI4h486AAE4gQM6AAE4gAKhBVgYA67MRRZiVJqwDRCDdRrGMm5o/lDTLRMFWFRCz3VApxYo+BUoOlEZa0pqZV34F14MjdgYDU+OQ+gmM8pLOPiaGAHHZ9Iyl1DKThsxkQF1za+BXbE047lDbcHYCwfmw7hIgXagQIGUo3XGU60yj98="}
        """;

    // A shared key, RSA-OAEP-SHA1.
    public const string SharedKey =
        """
        {"type":"unsigned-shared-key","wrappedKey":"4.cj+yfg7Xe/p7dHTel7/FUF4O62ZgowhHLrU2Cq7piEj4uAgrX9Kux0r747c0jOdu/pYv4HU0Pdu0L0GFEDV64jNOyIsjt8oIYBdFT6Zk5dWt1giqQ9+ECfZ7HQn7Hu6bS8Qm1jzGAoO7QRRAkN+HeG8siXRJeotoJHi0pEBOe/qG4Ticc6yi0kWW0RHuXcc/nDiY8HQWGUlGx4Oc2pgwnUzudR6oOgRcLh9WPL8Fg1As4K4qYANYGdDb0RGV5qdfvJx/a4MBvLwLg37XtLxhO+laH6QqZWYeUdFAZLwRJO5v0MA8WpbdBLHU1JjvognLPuq3Djwd6US2VvuGxsL8CA=="}
        """;

    // A security state signed by VerifyingKeyId.
    public const string SecurityState =
        """
        {"signedSecurityState":"hFgepAEnAxg8BFAHb54KmAZsttQnP+OHlulMOgABOH8CoEqhZ3ZlcnNpb24CWEB+KzuLaJQvJ0YzF6ajxzaRtknGd7WfY/PVsmLfUaUY4RDCdKhYVXVkxnn7NiNt6nuWOa/9TofJulcMaAseWNcO","type":"signed-security-state"}
        """;

    // A raw master password unlock with Argon2id; its wrappedKey is Envelope.
    public const string MasterPassword =
        """
        {"kdf":{"iterations":3,"memory":64,"parallelism":4,"salt":"user@example.com","type":"argon2id"},"kind":"master-password","type":"raw","wrappedKey":"g1g+pgE6AAEReQMYZQRQaWjhDrmAIpcJBWKwIj3ogDoAARVcUErIpAnk8yuVGBzCrVI4h486AAE4gQM6AAE4gAKhBVgYA67MRRZiVJqwDRCDdRrGMm5o/lDTLRMFWFRCz3VApxYo+BUoOlEZa0pqZV34F14MjdgYDU+OQ+gmM8pLOPiaGAHHZ9Iyl1DKThsxkQF1za+BXbE047lDbcHYCwfmw7hIgXagQIGUo3XGU60yj98="}
        """;

    // A device unlock.
    public const string Device =
        """
        {"kind":"device","wrappedKey":"4.cj+yfg7Xe/p7dHTel7/FUF4O62ZgowhHLrU2Cq7piEj4uAgrX9Kux0r747c0jOdu/pYv4HU0Pdu0L0GFEDV64jNOyIsjt8oIYBdFT6Zk5dWt1giqQ9+ECfZ7HQn7Hu6bS8Qm1jzGAoO7QRRAkN+HeG8siXRJeotoJHi0pEBOe/qG4Ticc6yi0kWW0RHuXcc/nDiY8HQWGUlGx4Oc2pgwnUzudR6oOgRcLh9WPL8Fg1As4K4qYANYGdDb0RGV5qdfvJx/a4MBvLwLg37XtLxhO+laH6QqZWYeUdFAZLwRJO5v0MA8WpbdBLHU1JjvognLPuq3Djwd6US2VvuGxsL8CA==","wrappedPrivateKey":"7.g1gcowE6AAEReQMYZQRQaWjhDrmAIpcJBWKwIj3ogKEFWBibXEwdYzQKzeR1g34Khp+MNvKqUIhimypYVBpSgXh14slMH7gN0AgEJ+BF1APT4DUoWkE87m3kmFoSKyjkQguYdoH8idmvwWKBEdLFCFZ/AGM6SheLPIxrowven1YH+g7TZl5Q+Xza1/sjquC9bw==","wrappedPublicKey":"7.g1g/owE6AAEReQN4I2FwcGxpY2F0aW9uL3guYml0d2FyZGVuLnV0ZjgtcGFkZGVkBFBKyKQJ5PMrlRgcwq1SOIePoQVYGI3SyWtFtwtku3ma4RKvCO7CNXN21l36z1gwW0gI0cqju7UQkG3SW73JW57yu98fsQe7iWQNXEltkQL4Y9mhAuspXAYjQqF+erxl"}
        """;
}
