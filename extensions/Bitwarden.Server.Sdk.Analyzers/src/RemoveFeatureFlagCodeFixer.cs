using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

            if (!diagnostic.Properties.TryGetValue("flagKey", out var flagKey))
            {
                continue;
            }

            if (!diagnostic.Properties.TryGetValue("removalHint", out var removalHint) && !string.IsNullOrEmpty(removalHint))
            {
                continue;
            }

            if (string.IsNullOrEmpty(flagKey))
            {
                continue;
            }

            context.RegisterCodeFix(CodeAction.Create(
                "Remove feature flag",
                createChangedDocument: (t) => RemoveFlagAsync(context.Document, diagnostic.Location, removalHint!, t),
                equivalenceKey: $"BW0002-{flagKey}"
            ), diagnostic);
        }

        return Task.CompletedTask;
    }

    public sealed override FixAllProvider? GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    private async Task<Document> RemoveFlagAsync(Document document, Location location, string removalHint, CancellationToken token)
    {
        var root = await document.GetSyntaxRootAsync(token);

        if (root == null)
        {
            throw new InvalidOperationException("Syntax root could not be retrieved.");
        }

        var node = root.FindNode(location.SourceSpan);

        if (removalHint == "isEnabledCheck")
        {
            return RemoveIsEnabledCheck(document, root, node);
        }
        else if (removalHint == "requireFeatureAttribute")
        {
            return RemoveRequireFeatureAttribute(document, root, node);
        }
        else if (removalHint == "requireFeatureMethod")
        {
            return RemoveRequireFeatureMethod(document, root, node);
        }

        throw new InvalidOperationException($"Invalid removal hint: {removalHint}");
    }

    private static Document RemoveIsEnabledCheck(Document document, SyntaxNode root, SyntaxNode node)
    {
        SyntaxNode? newSyntax = null;

        if (node.Parent is BinaryExpressionSyntax binaryExpression)
        {
            newSyntax = root.ReplaceNode(node.Parent, SimplifyBinary(binaryExpression, node));
        }
        else if (node.Parent is PrefixUnaryExpressionSyntax unaryExpression && unaryExpression.IsKind(SyntaxKind.LogicalNotExpression))
        {
            // If this is the only thing in the if statement, erase the if block and make it only the else block
            if (unaryExpression.Parent is IfStatementSyntax ifStatement)
            {
                newSyntax = root.ReplaceNode(ifStatement, ifStatement.Else?.Statement.ChildNodes().Select(n => n.WithLeadingTrivia(ifStatement.GetLeadingTrivia())) ?? []);
            }
        }
        else if (node.Parent is IfStatementSyntax ifStatement)
        {
            // The feature check was the only thing in the if statement, replace the whole thing with it's block
            newSyntax = root.ReplaceNode(ifStatement, ifStatement.Statement.ChildNodes().Select(n => n.WithLeadingTrivia(node.Parent.GetLeadingTrivia())));
        }

        // If we don't have any other special cases, just replace the check with a `true` literal.
        return document.WithSyntaxRoot(newSyntax ?? root.ReplaceNode(node, SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)));
    }

    private static Document RemoveRequireFeatureAttribute(Document document, SyntaxNode root, SyntaxNode node)
    {
        SyntaxNode newSyntax;
        if (node.Parent is AttributeListSyntax attributeList)
        {
            if (attributeList.Attributes.Count == 1)
            {
                // Remove the whole attribute list if this was the only attribute in it
                newSyntax = root.RemoveNode(node.Parent, SyntaxRemoveOptions.KeepNoTrivia)!;
            }
            else
            {
                // If there are multiple attribute in the list, just remove ours
                newSyntax = root.RemoveNode(node, SyntaxRemoveOptions.KeepNoTrivia)!;
            }
        }
        else
        {
            throw new InvalidOperationException($"Unhandled parent kind to attribute {node.Parent?.RawKind.ToString() ?? "null"}");
        }

        return document.WithSyntaxRoot(newSyntax);
    }

    private static Document RemoveRequireFeatureMethod(Document document, SyntaxNode root, SyntaxNode node)
    {

        return document.WithSyntaxRoot(root.ReplaceNode(node.Parent!, node.Parent.RemoveNode(node, SyntaxRemoveOptions.KeepNoTrivia)!));
    }

    private static SyntaxNode SimplifyBinary(BinaryExpressionSyntax binaryExpression, SyntaxNode targetNode)
    {
        if (binaryExpression.Left == targetNode)
        {
            return binaryExpression.Right.WithoutTrivia();
        }

        return binaryExpression.Left.WithoutTrivia();
    }
}
