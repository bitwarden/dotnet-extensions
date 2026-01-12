using System.CodeDom.Compiler;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Bitwarden.Server.Sdk.Features.Analyzers;

public class FeaturesGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<FlagKeyCollectionSpec> flagKeyClasses = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Bitwarden.Server.Sdk.Features.FlagKeyCollectionAttribute",
            predicate: (_, _) => true,
            transform: FlagKeyCollectionSpec.Create
        )
            .Where(f => f is not null)!;

        context.RegisterSourceOutput(flagKeyClasses, (context, spec) =>
        {
            foreach (var field in spec.Fields)
            {
                context.ReportDiagnostic(Diagnostic.Create(FeatureFlagAnalyzer._removeFeatureFlagRule, field.Location, field.Name));
            }

            var sw = new StringWriter();
            var writer = new IndentedTextWriter(sw);
            spec.Write(writer);
            context.AddSource($"{spec.Type.FilenameHint}.FlagKeyCollection.g.cs", SourceText.From(sw.ToString(), Encoding.UTF8));
        });
    }
}
