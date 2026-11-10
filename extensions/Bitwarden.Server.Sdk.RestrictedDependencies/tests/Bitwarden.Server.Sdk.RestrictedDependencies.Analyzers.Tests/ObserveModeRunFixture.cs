using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests;

/// <summary>
/// One observe-mode run of the sample project over <see cref="ToolHost"/>, shared by the tests
/// that each assert on a different row shape it produced. The run is the same for all of them, so
/// doing it once per class keeps them from compiling the sample project again per test.
/// </summary>
public sealed class ObserveModeRunFixture : IAsyncLifetime
{
    /// <summary>
    /// Every diagnostic the run produced, including the BW0017 observation rows.
    /// </summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; private set; }

    public async ValueTask InitializeAsync() =>
        Diagnostics = await ToolHost.RunAsync(observe: true, enableObservations: true);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
