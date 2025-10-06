using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;

namespace Bitwarden.Server.Sdk.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveFeatureFlagCodeFixer))]
public class RemoveFeatureFlagCodeFixer : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("BW0002");

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            if (diagnostic.Id != "BW0002")
            {
                // I don't think we'll actually get other diagnostics
                Debug.Fail($"Other diagnostic found: {diagnostic.Id}");
                continue;
            }

            if (!diagnostic.Properties.TryGetValue("flagName", out var flagName))
            {
                continue;
            }

            if (string.IsNullOrEmpty(flagName))
            {
                continue;
            }

            context.RegisterCodeFix(CodeAction.Create(
                "Remove feature flag",
                createChangedDocument: (t) => RemoveFlagAsync(context.Document, diagnostic.Location, flagName!, t),
                equivalenceKey: flagName
            ), diagnostic);
        }

        return Task.CompletedTask;
    }

    public sealed override FixAllProvider? GetFixAllProvider()
    {
        // TODO: this might not be the one we want
        return WellKnownFixAllProviders.BatchFixer;
    }

    private async Task<Document> RemoveFlagAsync(Document document, Location location, string flagName, CancellationToken token)
    {
        var root = await document.GetSyntaxRootAsync(token);

        if (root == null)
        {
            throw new InvalidOperationException("Syntax root could not be retrieved.");
        }

        var node = root.FindNode(location.SourceSpan);

        var simplified = root.ReplaceNode(node, SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression));

        return document.WithSyntaxRoot(simplified);
    }
    

}
