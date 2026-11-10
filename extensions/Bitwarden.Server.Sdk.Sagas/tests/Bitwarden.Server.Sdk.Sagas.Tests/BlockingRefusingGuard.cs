namespace Bitwarden.Server.Sdk.Sagas.Tests;

// A guard that, once told, waits until released and then refuses, so a test can make a write while it decides.
internal sealed class BlockingRefusingGuard : IServiceWritesGuard
{
    public ManualResetEventSlim Entered { get; } = new();

    public ManualResetEventSlim Release { get; } = new();

    public void Spanned(string operation, IReadOnlyList<string> steps, SpanDeclarationAttribute? declared)
    {
        Entered.Set();
        Release.Wait(TimeSpan.FromSeconds(30));

        throw new InvalidOperationException("Refused.");
    }
}
