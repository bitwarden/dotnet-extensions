namespace Bitwarden.Server.Sdk.Crypto.Tests;

/// <summary>
/// Inputs mirrored from the bitwarden-uniffi-server Rust tests in sdk-internal, so both sides agree.
/// <see cref="CoseEncString"/> is encrypted with, and <see cref="Envelope"/> seals, the
/// XAES-256-GCM key <see cref="KeyId"/>.
/// </summary>
internal static class Fixtures
{
    public const string KeyId = "786b476843503fe1f6deb3951ef8f037";

    public const string CoseEncString =
        "7.g1g/owE6AAEReQN4I2FwcGxpY2F0aW9uL3guYml0d2FyZGVuLnV0ZjgtcGFkZGVkBFB4a0doQ1A/4fbes5Ue+PA3oQVYGDyfZwmpm8qoMQ6T/GkNaSNiH0xURegyi1gwqCiYSKv1pO6JGK65CMOYx4dAUaqpKz5JlvFE/XdTX6VcOggf2PX0H9iCB/rDjvng";

    public const string LegacyEncString =
        "2.lydV844NLeMlZyw+lBvavQ==|g/ybWBsaTkyTDJYL1kcrZQ==|2ZeoX1YiRQoaowBuGS7feAH9PZvz3VDy6QuPtMNu7R4=";

    public const string Envelope =
        "g1g+pgE6AAEReQMYZQRQ8UUgSvckp9zxEdxy+99QTDoAARVcUHhrR2hDUD/h9t6zlR748Dc6AAE4gQM6AAE4gAKhBVgYHYM4amZXcPx+ioIiaBQXy/itml3h/5kpWFR2SQ9uMP9TBs/iL2+tuzwOcGtphqCkEpSBBlifIzT9oow7+hYJ9N4hKxXYLEn0ldNDdfi7ryjXgAscY0OTDPOEIwUaqJZGKESUEDDhohHVoaGgZrY=";
}
