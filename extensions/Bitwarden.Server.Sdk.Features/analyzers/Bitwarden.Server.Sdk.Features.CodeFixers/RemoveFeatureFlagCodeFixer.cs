using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;

namespace Bitwarden.Server.Sdk.CodeFixers;

internal record MockReturnInfo(bool ReturnsFalse, MemberDeclarationSyntax? TestMethod, ExpressionStatementSyntax? ExpressionStatement);

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveFeatureFlagCodeFixer))]
public class RemoveFeatureFlagCodeFixer : CodeFixProvider
{
    private const string IsEnabledMethodName = "IsEnabled";
    private const string RequireFeatureMethodName = "RequireFeature";
    private const string RequireFeatureAttributeName = "RequireFeature";
    private const string MockReturnsMethodName = "Returns";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("BW0001");

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue("FlagKey", out var flagKey) || string.IsNullOrEmpty(flagKey))
            {
                Debug.Fail($"We failed to add a flagKey property to a BW0001 diagnostic at {diagnostic.Location}");
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

    public sealed override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    private static async Task<Solution> RemoveFlagAsync(Document document, Location location, CancellationToken token)
    {
        var solution = document.Project.Solution;
        var root = await document.GetSyntaxRootAsync(token)
            ?? throw new InvalidOperationException("Syntax root could not be retrieved.");

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
            var referenceDocument = solution.GetDocument(documentGroup.Key);

            if (referenceDocument is null)
            {
                // This may be a source generated file
                continue;
            }

            var referenceRoot = await referenceDocument.GetSyntaxRootAsync(token);
            if (referenceRoot is null)
            {
                continue;
            }

            var nodesToFix = documentGroup
                .Select(referenceLocation => referenceRoot.FindNode(referenceLocation.Location.SourceSpan))
                .ToList();

            // Pre-analyze nodes that need semantic information before we start modifying the tree
            var docSemanticModel = await referenceDocument.GetSemanticModelAsync(token);
            var mockAnalysis = new Dictionary<SyntaxNode, MockReturnInfo>();

            if (docSemanticModel is not null)
            {
                foreach (var nodeToAnalyze in nodesToFix)
                {
                    var mockInfo = AnalyzeMockReturns(nodeToAnalyze, docSemanticModel);
                    if (mockInfo is not null)
                    {
                        mockAnalysis[nodeToAnalyze] = mockInfo;
                    }
                }
            }

            // Use TrackNodes to maintain node identity across replacements
            var currentRoot = referenceRoot.TrackNodes(nodesToFix);

            // Apply fixes sequentially, using GetCurrentNode to get the updated node after each change
            foreach (var originalNode in nodesToFix)
            {
                var trackedNode = currentRoot.GetCurrentNode(originalNode);
                if (trackedNode != null)
                {
                    mockAnalysis.TryGetValue(originalNode, out var mockInfo);
                    currentRoot = FixFlagUsage(currentRoot, trackedNode, mockInfo);
                }
            }

            var updatedDocument = solution.GetDocument(referenceDocument.Id)!.WithSyntaxRoot(currentRoot);
            updatedDocument = await Formatter.OrganizeImportsAsync(updatedDocument, token);
            solution = updatedDocument.Project.Solution;
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

    private static SyntaxNode FixFlagUsage(SyntaxNode root, SyntaxNode node, MockReturnInfo? mockInfo)
    {
        // Check for invocation expressions (IsEnabled or RequireFeature method calls)
        if (node.FirstAncestorOrSelf<InvocationExpressionSyntax>() is { } invocationExpression
            && invocationExpression.Expression is MemberAccessExpressionSyntax memberAccessExpression)
        {
            var methodName = memberAccessExpression.Name.Identifier.Text;

            if (methodName == IsEnabledMethodName)
            {
                return RemoveIsEnabledCheck(root, invocationExpression, mockInfo);
            }

            if (methodName == RequireFeatureMethodName)
            {
                return RemoveRequireFeatureMethod(root, invocationExpression);
            }
        }

        // Check for [RequireFeature] attributes on controllers
        if (node.FirstAncestorOrSelf<AttributeSyntax>() is { } attributeSyntax
            && attributeSyntax.Name.ToString().Contains(RequireFeatureAttributeName))
        {
            return RemoveRequireFeatureAttribute(root, attributeSyntax);
        }

        return root;
    }

    private static SyntaxNode RemoveIsEnabledCheck(SyntaxNode root, SyntaxNode node, MockReturnInfo? mockInfo)
    {
        SyntaxNode? newSyntax = node.Parent switch
        {
            BinaryExpressionSyntax binaryExpression =>
                root.ReplaceNode(binaryExpression, SimplifyBinary(binaryExpression, node)),

            PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression, Parent: IfStatementSyntax ifStatement } =>
                root.ReplaceNode(ifStatement, GetStatements(ifStatement.Else?.Statement, ifStatement.GetLeadingTrivia())),

            IfStatementSyntax ifStatement =>
                root.ReplaceNode(ifStatement, GetStatements(ifStatement.Statement, ifStatement.GetLeadingTrivia())),

            _ => null
        };

        if (newSyntax is null && mockInfo is not null)
        {
            if (mockInfo.ReturnsFalse && mockInfo.TestMethod is not null)
            {
                // Delete the whole test
                var testMethodInRoot = root.FindNode(mockInfo.TestMethod.Span);
                if (testMethodInRoot is not null)
                {
                    newSyntax = root.RemoveNode(testMethodInRoot, SyntaxRemoveOptions.KeepNoTrivia);
                }
            }
            else if (mockInfo.ExpressionStatement is not null)
            {
                // Delete the whole expression of the mock
                var expressionInRoot = root.FindNode(mockInfo.ExpressionStatement.Span);
                if (expressionInRoot is not null)
                {
                    newSyntax = root.RemoveNode(expressionInRoot, SyntaxRemoveOptions.KeepTrailingTrivia);
                }
            }
        }

        // If we don't have any other special cases, just replace the check with a `true` literal.
        var resultRoot = newSyntax ?? root.ReplaceNode(node, SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression));

        // Simplify any expressions that now contain literal boolean values
        return SimplifyBooleanExpressions(resultRoot);
    }

    private static SyntaxNode SimplifyBooleanExpressions(SyntaxNode root)
    {
        // Find all ternary expressions with literal boolean conditions
        var ternariesToSimplify = root.DescendantNodes()
            .OfType<ConditionalExpressionSyntax>()
            .Where(ternary => ternary.Condition is LiteralExpressionSyntax { } literal &&
                             (literal.IsKind(SyntaxKind.TrueLiteralExpression) || literal.IsKind(SyntaxKind.FalseLiteralExpression)))
            .ToList();

        foreach (var ternary in ternariesToSimplify)
        {
            var condition = (LiteralExpressionSyntax)ternary.Condition;
            var replacement = condition.IsKind(SyntaxKind.TrueLiteralExpression)
                ? ternary.WhenTrue.WithTriviaFrom(ternary)
                : ternary.WhenFalse.WithTriviaFrom(ternary);

            root = root.ReplaceNode(ternary, replacement);
        }

        // Find all negations of literal booleans (!true or !false)
        var negationsToSimplify = root.DescendantNodes()
            .OfType<PrefixUnaryExpressionSyntax>()
            .Where(negation => negation.IsKind(SyntaxKind.LogicalNotExpression) &&
                              negation.Operand is LiteralExpressionSyntax literal &&
                              (literal.IsKind(SyntaxKind.TrueLiteralExpression) || literal.IsKind(SyntaxKind.FalseLiteralExpression)))
            .ToList();

        foreach (var negation in negationsToSimplify)
        {
            var operand = (LiteralExpressionSyntax)negation.Operand;
            var replacement = operand.IsKind(SyntaxKind.TrueLiteralExpression)
                ? SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression).WithTriviaFrom(negation)
                : SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression).WithTriviaFrom(negation);

            root = root.ReplaceNode(negation, replacement);
        }

        // Remove unreachable statements after return/throw in blocks
        return RemoveUnreachableCode(root);
    }

    private static SyntaxNode RemoveUnreachableCode(SyntaxNode root)
    {
        var blocksToFix = root.DescendantNodes()
            .OfType<BlockSyntax>()
            .Where(HasUnreachableStatements)
            .ToList();

        foreach (var block in blocksToFix)
        {
            var statements = block.Statements;
            var firstUnreachableIndex = -1;

            for (var i = 0; i < statements.Count; i++)
            {
                if (statements[i] is ReturnStatementSyntax or ThrowStatementSyntax)
                {
                    firstUnreachableIndex = i + 1;
                    break;
                }
            }

            if (firstUnreachableIndex > 0)
            {
                var newBlock = block.WithStatements(SyntaxFactory.List(statements.Take(firstUnreachableIndex)));
                root = root.ReplaceNode(block, newBlock);
            }
        }

        return root;
    }

    private static bool HasUnreachableStatements(BlockSyntax block)
    {
        var statements = block.Statements;
        for (var i = 0; i < statements.Count - 1; i++)
        {
            if (statements[i] is ReturnStatementSyntax or ThrowStatementSyntax)
            {
                return true;
            }
        }
        return false;
    }

    private static SyntaxNode RemoveRequireFeatureAttribute(SyntaxNode root, SyntaxNode node)
    {
        var attributeList = (AttributeListSyntax)node.Parent!;

        // Remove the whole attribute list if this was the only attribute, otherwise just remove ours
        var nodeToRemove = attributeList.Attributes.Count == 1 ? (SyntaxNode)attributeList : node;
        return root.RemoveNode(nodeToRemove, SyntaxRemoveOptions.KeepNoTrivia)!;
    }

    private static SyntaxNode RemoveRequireFeatureMethod(SyntaxNode root, SyntaxNode node)
    {
        var invocationExpression = (InvocationExpressionSyntax)node;
        var memberAccessExpression = (MemberAccessExpressionSyntax)invocationExpression.Expression;

        // Check if we are at the end of a chain (e.g., app.MapGet(...).RequireFeature(Flag))
        var isEndOfChain = memberAccessExpression.Expression is IdentifierNameSyntax
                        && invocationExpression.Parent is not null
                        && !invocationExpression.Parent.ChildNodes().Skip(1).Any();

        if (isEndOfChain)
        {
            // In top-level code the ExpressionStatement is wrapped in a GlobalStatementSyntax;
            // in method bodies it is a direct child of the block. Remove the right level.
            var expressionStatement = invocationExpression.Parent!;
            var nodeToRemove = expressionStatement.Parent is GlobalStatementSyntax
                ? expressionStatement.Parent
                : expressionStatement;
            return RemoveNodeCleanly(root, nodeToRemove);
        }

        // We're in the middle of a chain (e.g., app.MapGet(...).RequireFeature(Flag).RequireAuthorization())
        // Just remove this method call from the chain
        return root.ReplaceNode(
            invocationExpression,
            memberAccessExpression.Expression.WithTriviaFrom(invocationExpression)
        );
    }

    private static IEnumerable<SyntaxNode> GetStatements(StatementSyntax? statement, SyntaxTriviaList leadingTrivia) =>
        statement is null ? [] :
        statement is BlockSyntax block
            ? block.Statements.Select(s => (SyntaxNode)s.WithLeadingTrivia(leadingTrivia))
            : [statement.WithLeadingTrivia(leadingTrivia)];

    private static SyntaxNode SimplifyBinary(BinaryExpressionSyntax binaryExpression, SyntaxNode targetNode) =>
        binaryExpression.Left == targetNode
            ? binaryExpression.Right.WithTriviaFrom(binaryExpression.Left)
            : binaryExpression.Left.WithTriviaFrom(binaryExpression.Right);

    private static BlockSyntax CreateEmptyBlock(BlockSyntax originalBlock)
    {
        var indentation = originalBlock.CloseBraceToken.LeadingTrivia
            .Where(t => t.IsKind(SyntaxKind.WhitespaceTrivia));

        return SyntaxFactory.Block()
            .WithOpenBraceToken(SyntaxFactory.Token(
                originalBlock.OpenBraceToken.LeadingTrivia,
                SyntaxKind.OpenBraceToken,
                SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine("\n"))))
            .WithCloseBraceToken(SyntaxFactory.Token(
                SyntaxFactory.TriviaList(indentation),
                SyntaxKind.CloseBraceToken,
                originalBlock.CloseBraceToken.TrailingTrivia));
    }

    private static SyntaxNode RemoveNodeCleanly(SyntaxNode root, SyntaxNode nodeToRemove)
    {
        var parent = nodeToRemove.Parent!;
        var siblings = parent.ChildNodes().ToList();
        var index = siblings.IndexOf(nodeToRemove);

        // Handle previous sibling cleanup
        if (index > 0)
        {
            return CleanupPreviousSiblingAndRemove(root, nodeToRemove, siblings[index - 1]);
        }

        // Handle next sibling cleanup (first child)
        if (index == 0 && siblings.Count > 1)
        {
            return CleanupNextSiblingAndRemove(root, nodeToRemove, siblings[1]);
        }

        // Handle empty block case
        if (siblings.Count == 1 && parent is BlockSyntax block)
        {
            return root.ReplaceNode(parent, CreateEmptyBlock(block));
        }

        // siblings.Count == 1 && parent is not BlockSyntax (e.g., braceless if body)
        return root.RemoveNode(nodeToRemove, SyntaxRemoveOptions.KeepNoTrivia)!;
    }

    private static SyntaxNode CleanupPreviousSiblingAndRemove(SyntaxNode root, SyntaxNode nodeToRemove, SyntaxNode prevSibling)
    {
        var trailingTrivia = prevSibling.GetTrailingTrivia();

        // Keep only non-whitespace trivia and at most one end-of-line
        var newTrailingTrivia = SyntaxFactory.TriviaList(
            trailingTrivia.TakeWhile(t => !t.IsKind(SyntaxKind.WhitespaceTrivia) && !t.IsKind(SyntaxKind.EndOfLineTrivia))
                .Concat(trailingTrivia.Where(t => t.IsKind(SyntaxKind.EndOfLineTrivia)).Take(1))
        );

        var newPrevSibling = prevSibling.WithTrailingTrivia(newTrailingTrivia);
        var newRoot = root.ReplaceNode(prevSibling, newPrevSibling);
        return newRoot.RemoveNode(newRoot.FindNode(nodeToRemove.Span), SyntaxRemoveOptions.KeepNoTrivia)!;
    }

    private static SyntaxNode CleanupNextSiblingAndRemove(SyntaxNode root, SyntaxNode nodeToRemove, SyntaxNode nextSibling)
    {
        // Preserve the original indentation from the node being removed
        var removedNodeIndentation = nodeToRemove.GetLeadingTrivia()
            .FirstOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));

        var leadingTrivia = nextSibling.GetLeadingTrivia();

        // Remove all leading whitespace and blank lines
        var nonWhitespaceTrivia = leadingTrivia
            .SkipWhile(t => t.IsKind(SyntaxKind.WhitespaceTrivia) || t.IsKind(SyntaxKind.EndOfLineTrivia));

        // Add back the original indentation
        var newLeadingTrivia = removedNodeIndentation.IsKind(SyntaxKind.None)
            ? SyntaxFactory.TriviaList(nonWhitespaceTrivia)
            : SyntaxFactory.TriviaList(removedNodeIndentation).AddRange(nonWhitespaceTrivia);

        var newNextSibling = nextSibling.WithLeadingTrivia(newLeadingTrivia);
        var newRoot = root.ReplaceNode(nextSibling, newNextSibling);
        return newRoot.RemoveNode(newRoot.FindNode(nodeToRemove.Span), SyntaxRemoveOptions.KeepNoTrivia)!;
    }

    private static MockReturnInfo? AnalyzeMockReturns(SyntaxNode node, SemanticModel semanticModel)
    {
        // Check pattern: featureService.IsEnabled(flag).Returns(false)
        if (node.FirstAncestorOrSelf<InvocationExpressionSyntax>() is not
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: IsEnabledMethodName },
                Parent: MemberAccessExpressionSyntax { Name.Identifier.Text: MockReturnsMethodName, Parent: { } returnsParent } returnsAccess
            })
        {
            return null;
        }

        if (semanticModel.GetOperation(returnsParent) is not IInvocationOperation { Arguments: [_, var secondArg, ..] })
        {
            return null;
        }

        var returnsFalse = secondArg.Value.ConstantValue is { HasValue: true, Value: false };
        var testMethod = returnsFalse ? returnsAccess.FirstAncestorOrSelf<MemberDeclarationSyntax>() : null;
        var expressionStatement = returnsParent.FirstAncestorOrSelf<ExpressionStatementSyntax>();

        return new MockReturnInfo(returnsFalse, testMethod, expressionStatement);
    }
}
