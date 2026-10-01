using System.Collections.Immutable;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis;
using Microsoft.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Diagnostics;

/// <summary>
/// BW0016. Finds any configuration that lowers a restricted-dependency diagnostic below error,
/// whether it came from compiler options, a global analyzer config, or an .editorconfig. BW0011 is
/// exempt because it is a warning by design, and BW0017 because the baseline tool turns it on.
/// </summary>
internal static class DiagnosticSeverityScanner
{
    /// <summary>
    /// One diagnostic per lowered id per configuration source, reported without a location because
    /// the configuration that lowered it is not in the compilation.
    /// </summary>
    public static ImmutableArray<Diagnostic> Run(Compilation compilation, string? repoRoot, CancellationToken cancellationToken)
    {
        var found = ImmutableArray.CreateBuilder<Diagnostic>();
        var options = compilation.Options;
        var provider = options.SyntaxTreeOptionsProvider;
        var seen = new HashSet<(string, string)>();

        foreach (var descriptor in DiagnosticDescriptors.MustRemainErrors)
        {
            if (options.SpecificDiagnosticOptions.TryGetValue(descriptor.Id, out var report) && IsLowered(report))
            {
                Lowered(descriptor.Id, report, "compiler options (NoWarn or WarningsNotAsErrors)");
            }

            if (provider is null)
            {
                continue;
            }

            if (provider.TryGetGlobalDiagnosticValue(descriptor.Id, cancellationToken, out report) && IsLowered(report))
            {
                Lowered(descriptor.Id, report, "a global analyzer config");
            }

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (provider.TryGetDiagnosticValue(tree, descriptor.Id, cancellationToken, out report) && IsLowered(report))
                {
                    var directory = tree.FilePath.Replace('\\', '/');
                    var slash = directory.LastIndexOf('/');
                    directory = slash >= 0 ? directory.Substring(0, slash + 1) : directory;
                    if (seen.Add((descriptor.Id, directory)))
                    {
                        Lowered(descriptor.Id, report, $"an .editorconfig applying to '{RepositoryPathNormalizer.ToRelative(directory, repoRoot)}'");
                    }
                }
            }
        }

        return found.ToImmutable();

        void Lowered(string id, ReportDiagnostic report, string source) =>
            found.Add(Diagnostic.Create(DiagnosticDescriptors.SeverityLowered, Location.None, id, report.ToString().ToLowerInvariant(), source));

        static bool IsLowered(ReportDiagnostic report) =>
            report is ReportDiagnostic.Suppress or ReportDiagnostic.Warn or ReportDiagnostic.Info or ReportDiagnostic.Hidden;
    }
}
