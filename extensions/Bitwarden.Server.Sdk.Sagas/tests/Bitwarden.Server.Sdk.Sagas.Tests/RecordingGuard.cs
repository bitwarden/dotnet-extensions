namespace Bitwarden.Server.Sdk.Sagas.Tests;

// A guard that remembers each time it is told, and refuses by throwing when asked to.
internal sealed class RecordingGuard : IServiceWritesGuard
{
    public bool Refuse { get; init; }

    public List<(string Operation, IReadOnlyList<string> Steps, SpanDeclarationAttribute? Declared)> Told { get; } = [];

    public void Spanned(string operation, IReadOnlyList<string> steps, SpanDeclarationAttribute? declared)
    {
        Told.Add((operation, steps, declared));

        if (Refuse)
        {
            throw new InvalidOperationException("Refused.");
        }
    }
}
