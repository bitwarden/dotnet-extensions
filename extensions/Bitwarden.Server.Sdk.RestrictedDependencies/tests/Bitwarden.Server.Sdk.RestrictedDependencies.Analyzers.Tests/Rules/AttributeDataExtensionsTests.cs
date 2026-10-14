using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests.Rules;

public class AttributeDataExtensionsTests
{
    [Fact]
    public void ReadStringArray_ExplicitNull_ReturnsEmpty()
    {
        var widget = Compile("""
            using Bitwarden.Server.Sdk.RestrictedDependencies;

            namespace Test;

            [RestrictedDependency(AllowedPaths = null)]
            public interface IWidget
            {
            }
            """).GetTypeByMetadataName("Test.IWidget")!;
        var attribute = RestrictedTypeModel.FindAttribute(widget)!;

        Assert.Empty(attribute.ReadStringArray("AllowedPaths"));
    }

    private static CSharpCompilation Compile(string source)
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return CSharpCompilation.Create(
            "AttributeDataExtensionsTest",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(AttributeConstants.Text, cancellationToken: TestContext.Current.CancellationToken),
                CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken),
            ],
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Join(runtimeDirectory, "System.Runtime.dll")),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
