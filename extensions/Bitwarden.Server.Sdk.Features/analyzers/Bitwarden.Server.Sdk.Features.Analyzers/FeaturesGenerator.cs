using System.CodeDom.Compiler;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Bitwarden.Server.Sdk.Features.Analyzers;

public class FeaturesGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var flagKeyClasses = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Bitwarden.Server.Sdk.Features.FlagKeyCollectionAttribute",
            predicate: (_, _) => true,
            transform: FlagKeyCollectionSpec.Create
        );

        context.RegisterSourceOutput(flagKeyClasses, (context, spec) =>
        {
            var sw = new StringWriter();
            var writer = new IndentedTextWriter(sw);
            spec.Write(writer);
            context.AddSource($"FlagKeyCollection.{spec.Type.FilenameHint}.g.cs", SourceText.From(sw.ToString(), Encoding.UTF8));
        });
    }
}
