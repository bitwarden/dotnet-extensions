using System.Runtime.CompilerServices;

namespace Bitwarden.Server.Sdk.Service.Tests.TestHost;

/// <summary>
/// Gives every host these tests build the secret a service needs outside <c>Development</c>
/// (<c>Idempotency:Secret</c>), as a deployed service has it: a fixed one, 32 bytes of 0x07, never the development
/// secret.
/// </summary>
/// <remarks>
/// A service reads the secret as its host starts and fails to start without it, and the hosts here are built in many
/// places, each in the <c>Testing</c> environment. Each reads the process's environment variables, so one variable,
/// set once before any test runs, reaches all of them.
/// </remarks>
internal static class IdempotencySecretSetting
{
    [ModuleInitializer]
    internal static void Set() =>
        System.Environment.SetEnvironmentVariable("Idempotency__Secret", Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()));
}
