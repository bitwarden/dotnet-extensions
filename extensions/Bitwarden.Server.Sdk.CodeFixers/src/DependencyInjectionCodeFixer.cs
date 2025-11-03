using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Simplification;

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

    private static async Task<Document> UpdateCallAsync(Document document, Location location, CancellationToken token)
    {
        var root = await document.GetSyntaxRootAsync(token);

        if (root == null)
        {
            return document;
        }

        var node = root.FindNode(location.SourceSpan);

        if (node is not InvocationExpressionSyntax invocationExpression)
        {
            return document;
        }

        if (invocationExpression.Expression is not MemberAccessExpressionSyntax ma)
        {
            return document;
        }

        var editor = await DocumentEditor.CreateAsync(document, token);
        var generator = editor.Generator;

        SyntaxNode memberName;
        if (ma.Name is GenericNameSyntax genericName)
        {
            // TODO: Use correct method
            memberName = generator.GenericName("TryAddSingleton", genericName.TypeArgumentList.Arguments);
        }
        else
        {
            // TODO: Use correct method
            memberName = generator.IdentifierName("TryAddSingleton");
        }

        var newNode = generator.InvocationExpression(
                generator.MemberAccessExpression(
                    generator.IdentifierName("Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions"),
                    memberName
                ),
                [ma.Expression, .. invocationExpression.ArgumentList.Arguments]
            );

        editor.ReplaceNode(node, newNode);

        var updatedDoc = editor.GetChangedDocument();

        updatedDoc = await ImportAdder.AddImportsAsync(updatedDoc, Simplifier.AddImportsAnnotation, cancellationToken: token);
        updatedDoc = await Simplifier.ReduceAsync(updatedDoc, Simplifier.Annotation, cancellationToken: token);

        return updatedDoc;
    }

    public override FixAllProvider? GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }
}
