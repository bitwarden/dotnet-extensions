using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

namespace Bitwarden.Server.Sdk.CodeFixers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DependencyInjectionCodeFixer))]
public sealed class DependencyInjectionCodeFixer : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("BW0003");

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            context.RegisterCodeFix(CodeAction.Create(
                "Use TryAdd overload",
                createChangedDocument: (t) => UpdateCallAsync(context.Document, diagnostic.Location, t),
                equivalenceKey: "BW0003",
                CodeActionPriority.Default
            ), diagnostic);
        }

        return Task.CompletedTask;
    }

    private static Task<Document> UpdateCallAsync(Document document, Location location, CancellationToken token)
    {
        return Task.FromResult(document);
    }

    public override FixAllProvider? GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }
}
