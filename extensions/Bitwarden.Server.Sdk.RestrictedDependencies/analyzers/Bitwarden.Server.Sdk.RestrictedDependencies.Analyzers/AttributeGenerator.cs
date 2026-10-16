using System.Text;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers;

/// <summary>
/// Emits the attributes into every compilation that has RestrictedDependencyAnalysis
/// enabled, so a production project can apply <c>[RestrictedDependency]</c> without referencing
/// anything. Projects out of scope get nothing, which is what keeps the copies from colliding
/// through InternalsVisibleTo.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class AttributeGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var enabled = context.AnalyzerConfigOptionsProvider
            .Select((provider, _) => RestrictedDependencyConfig.IsEnabled(provider.GlobalOptions));

        context.RegisterSourceOutput(enabled, static (productionContext, isEnabled) =>
        {
            if (isEnabled)
            {
                productionContext.AddSource(
                    AttributeConstants.HintName,
                    SourceText.From(AttributeConstants.Text, Encoding.UTF8));
            }
        });
    }
}
