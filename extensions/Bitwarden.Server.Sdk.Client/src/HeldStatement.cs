namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// What an <see cref="OutgoingCall"/> statement said, held so a read made as several calls states it for each
/// (<see cref="OutgoingCall.Hold"/>).
/// </summary>
public sealed class HeldStatement
{
    private readonly OutgoingCall.Stated? _stated;

    internal HeldStatement(OutgoingCall.Stated? stated) => _stated = stated;

    /// <summary>
    /// States what was held once more, for the next call made before the returned value is disposed; nothing when
    /// nothing was stated.
    /// </summary>
    /// <returns>Restores what was stated before, when disposed.</returns>
    public IDisposable Again() => _stated is { } stated ? OutgoingCall.Restate(stated) : Nothing.Instance;

    private sealed class Nothing : IDisposable
    {
        public static readonly Nothing Instance = new();

        public void Dispose()
        {
        }
    }
}
