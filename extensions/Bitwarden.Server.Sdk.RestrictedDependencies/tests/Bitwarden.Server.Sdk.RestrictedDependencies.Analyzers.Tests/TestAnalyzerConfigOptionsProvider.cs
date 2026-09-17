using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests;

/// <summary>
/// Stands in for the analyzer config a build makes compiler-visible: <paramref name="globalOptions"/>
/// carries the build_property values, and <paramref name="fileOptions"/> the per-file item metadata
/// that marks a baseline. Omitting <paramref name="fileOptions"/> reports nothing per file, which is
/// what a real build does for a file it did not mark.
/// </summary>
internal sealed class TestAnalyzerConfigOptionsProvider(
    IReadOnlyDictionary<string, string> globalOptions,
    IReadOnlyDictionary<string, string>? fileOptions = null) : AnalyzerConfigOptionsProvider
{
    private readonly AnalyzerConfigOptions _fileOptions = new Options(fileOptions ?? new Dictionary<string, string>());

    public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(globalOptions);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _fileOptions;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _fileOptions;

    private sealed class Options(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
    }
}
