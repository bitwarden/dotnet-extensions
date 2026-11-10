namespace Bitwarden.Server.Sdk.Crypto.Tests;

/// <summary>
/// Inputs mirrored from the bitwarden-uniffi-server Rust tests in sdk-internal, so both sides agree.
/// <see cref="CoseEncString"/> is encrypted with, and <see cref="Envelope"/> seals, the
/// XChaCha20-Poly1305 key <see cref="KeyId"/>.
/// </summary>
internal static class Fixtures
{
    public const string KeyId = "5367fc06796da8e62b61afdc5c02b9f1";

    public const string CoseEncString =
        "7.g1g/owE6AAERbwN4I2FwcGxpY2F0aW9uL3guYml0d2FyZGVuLnV0ZjgtcGFkZGVkBFBTZ/wGeW2o5ithr9xcArnxoQVYGK4ToRVEH/cowcY3KzAn0K4jqgl06xAWpVgwY5qRRgNeYMc9KUbZmghWPKJAz10ENbQdS0/u0XeWRDQz5TRXJiNl+TDyrlI6731b";

    public const string LegacyEncString =
        "2.lydV844NLeMlZyw+lBvavQ==|g/ybWBsaTkyTDJYL1kcrZQ==|2ZeoX1YiRQoaowBuGS7feAH9PZvz3VDy6QuPtMNu7R4=";

    public const string Envelope =
        "g1g+pgE6AAEReQMYZQRQNPLyvvUn/ZOFjUStHRQPvzoAARVcUFNn/AZ5bajmK2Gv3FwCufE6AAE4gQM6AAE4gAKhBVgYqKO9y+PFmZQr6GR5Nmj5bzc1uvqHm4FTWFRUAxgMKOBrC0T0iM0bqLFjzmBCkXA6fJ9qzG6e2iIWW4mJ7/j1+T/90Yi6syHtD+5mvda+rg6BF+BLzB48oItIfasxUcYthwzxVW33CfVk/JoNU+U=";
}
