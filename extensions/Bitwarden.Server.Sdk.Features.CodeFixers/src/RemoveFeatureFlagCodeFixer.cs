using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Operations;

namespace Bitwarden.Server.Sdk.CodeFixers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveFeatureFlagCodeFixer))]
public class RemoveFeatureFlagCodeFixer : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("BW0001");

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue("FlagKey", out var flagKey) || string.IsNullOrEmpty(flagKey))
            {
                Debug.Fail($"We failed to add a flagKey property to a BW0002 diagnostic at {diagnostic.Location}");
                continue;
            }

            context.RegisterCodeFix(CodeAction.Create(
                $"Remove '{flagKey}' feature.",
                createChangedSolution: (token) => RemoveFlagAsync(context.Document, diagnostic.Location, token),
                equivalenceKey: $"BW0001-{flagKey}",
                CodeActionPriority.Default
            ), diagnostic);
        }

        return Task.CompletedTask;
    }

    public sealed override FixAllProvider? GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    private static async Task<Solution> RemoveFlagAsync(Document document, Location location, CancellationToken token)
    {
        var solution = document.Project.Solution;
        var root = await document.GetSyntaxRootAsync(token);

        if (root is null)
        {
            throw new InvalidOperationException("Syntax root could not be retrieved.");
        }

        var node = root.FindNode(location.SourceSpan);

        if (node is not VariableDeclaratorSyntax variable)
        {
            throw new InvalidOperationException("Diagnostic should have been put on a field reference.");
        }

        var semanticModel = await document.GetSemanticModelAsync(token);

        if (semanticModel is null)
        {
            return solution;
        }

        var fieldSymbol = semanticModel.GetDeclaredSymbol(variable);

        if (fieldSymbol is null)
        {
            return solution;
        }

        var references = await SymbolFinder.FindReferencesAsync(fieldSymbol, document.Project.Solution, token);

        // Group references by document to process all changes in a single pass
        var referencesByDocument = references
            .SelectMany(r => r.Locations)
            .GroupBy(loc => loc.Document.Id);

        foreach (var documentGroup in referencesByDocument)
        {
            var referenceDocument = solution.GetDocument(documentGroup.Key)!;
            var referenceRoot = await referenceDocument.GetSyntaxRootAsync(token);
            if (referenceRoot is null)
            {
                continue;
            }

            // Collect all nodes that need fixing
            var nodesToFix = documentGroup
                .Select(referenceLocation => referenceRoot.FindNode(referenceLocation.Location.SourceSpan))
                .ToList();

            // Use TrackNodes to maintain node identity across replacements
            var currentRoot = referenceRoot.TrackNodes(nodesToFix);

            // Apply fixes sequentially, using GetCurrentNode to get the updated node after each change
            foreach (var originalNode in nodesToFix)
            {
                var trackedNode = currentRoot.GetCurrentNode(originalNode);
                if (trackedNode != null)
                {
                    currentRoot = await FixFlagUsageAsync(referenceDocument, currentRoot, trackedNode, token);
                }
            }

            solution = solution.WithDocumentSyntaxRoot(referenceDocument.Id, currentRoot);
        }

        var fieldDeclaration = node.FirstAncestorOrSelf<FieldDeclarationSyntax>();

        if (fieldDeclaration is null)
        {
            return solution;
        }

        // Now delete the field
        root = root.RemoveNode(fieldDeclaration, SyntaxRemoveOptions.KeepNoTrivia);

        return solution.WithDocumentSyntaxRoot(document.Id, root!);
    }

    private static async Task<SyntaxNode> FixFlagUsageAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken cancellationToken)
    {
        // Possibly IFeatureService.IsEnabled(Thing)
        // TODO: Will this also trigger on .RequireFeature for minimal APIs
        var invocationExpression = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();

        if (invocationExpression is not null)
        {
            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccessExpressionSyntax)
            {
                return root;
            }

            if (memberAccessExpressionSyntax.Name.Identifier.Text == "IsEnabled")
            {
                // TODO: Validate further
                return await RemoveIsEnabledCheckAsync(document, root, invocationExpression, cancellationToken);
            }
        }

        // Maybe [RequireFeature] on controllers
        var attributeSyntax = node.FirstAncestorOrSelf<AttributeSyntax>();

        if (attributeSyntax is not null)
        {

        }

        return root;
    }

    private static async Task<SyntaxNode> RemoveIsEnabledCheckAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken token)
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
        else if (node.Parent is MemberAccessExpressionSyntax { Name.Identifier.Text: "Returns" } memberAccess)
        {
            // They are chaining something after the check: IsEnabled(Flag).Returns(true); which is more than
            // likely a call to mock the return of this method, if they are mocking the return of constant `true`
            // the entire call should be able to disappear and the test would function
            // if they are returning a constant of `false`. The entire test should now be defunct and can be deleted
            // if they are not returning a constant, then plop an error in the test so that manual intervention becomes
            // needed.
            var semanticModel = await document.GetSemanticModelAsync(token);

            if (semanticModel is null)
            {
                return root;
            }

            if (memberAccess.Parent is null)
            {
                return root;
            }

            var memberAccessOperation = semanticModel.GetOperation(memberAccess.Parent);

            if (memberAccessOperation is not IInvocationOperation invocationOperation)
            {
                return root;
            }

            // First arg is the `this` parameter of the extension method
            if (invocationOperation.Arguments is not [_, var secondArg, ..])
            {
                return root;
            }

            if (secondArg.Value.ConstantValue.HasValue && secondArg.Value.ConstantValue.Value is false)
            {
                // Delete the whole test
                var testMethod = memberAccess.Ancestors()
                    .OfType<MemberDeclarationSyntax>()
                    .FirstOrDefault();

                if (testMethod == null)
                {
                    return root;
                }

                newSyntax = root.RemoveNode(testMethod, SyntaxRemoveOptions.KeepNoTrivia);
            }
            else
            {
                // Delete the whole expression of the mock
                var wholeExpression = memberAccess.Parent.FirstAncestorOrSelf<ExpressionStatementSyntax>();

                if (wholeExpression is null)
                {
                    return root;
                }

                newSyntax = root.RemoveNode(wholeExpression, SyntaxRemoveOptions.KeepTrailingTrivia);
            }
        }

        // If we don't have any other special cases, just replace the check with a `true` literal.
        return newSyntax ?? root.ReplaceNode(node, SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression));
    }

    private static Document RemoveRequireFeatureAttribute(Document document, SyntaxNode root, SyntaxNode node)
    {
        if (node.Parent is not AttributeListSyntax attributeList)
        {
            // When would this happen?
            Debug.Fail($"Attribute parent is not AttributeListSyntax it is instead of type {node.Parent?.GetType().FullName ?? "null"}");
            return document;
        }

        SyntaxNode newSyntax;
        if (attributeList.Attributes.Count == 1)
        {
            // Remove the whole attribute list if this was the only attribute in it
            newSyntax = root.RemoveNode(node.Parent, SyntaxRemoveOptions.KeepEndOfLine)!;
        }
        else
        {
            // If there are multiple attribute in the list, just remove ours
            newSyntax = root.RemoveNode(node, SyntaxRemoveOptions.KeepNoTrivia)!;
        }

        return document.WithSyntaxRoot(newSyntax);
    }

    private static Document RemoveRequireFeatureMethod(Document document, SyntaxNode root, SyntaxNode node)
    {
        var invocationExpression = (InvocationExpressionSyntax)node;
        var memberAccessExpression = (MemberAccessExpressionSyntax)invocationExpression.Expression;

        // Check if we are in the middle of a chain i.e: app.MapGet(...).RequireFeature(Flag).RequireAuthorization();
        if (memberAccessExpression.Expression is IdentifierNameSyntax
            && invocationExpression.Parent != null
            // If it has no siblings that means no one is chaining off of me
            && !invocationExpression.Parent.ChildNodes().Skip(1).Any())
        {
            // We are referencing a variable
            return document.WithSyntaxRoot(root.RemoveNode(
                invocationExpression.Parent.Parent!, SyntaxRemoveOptions.KeepNoTrivia)!
            );
        }

        // Our call is chained with another call i.e: app.MapGet(...).RequireFeature(Flag);
        return document.WithSyntaxRoot(root.ReplaceNode(
            invocationExpression,
            memberAccessExpression.Expression.WithTriviaFrom(invocationExpression)
        ));
    }

    private static SyntaxNode SimplifyBinary(BinaryExpressionSyntax binaryExpression, SyntaxNode targetNode)
    {
        if (binaryExpression.Left == targetNode)
        {
            return binaryExpression.Right.WithTriviaFrom(binaryExpression.Left);
        }

        return binaryExpression.Left.WithTriviaFrom(binaryExpression.Right);
    }
}
