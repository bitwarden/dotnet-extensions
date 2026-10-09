using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests.Analysis;

public class RepositoryPathNormalizerTests
{
    [Theory]
    [InlineData("/repo/", "/repo/src/Core/Foo.cs", "src/Core/Foo.cs")]
    [InlineData("/repo", "/repo/src/Core/Foo.cs", "src/Core/Foo.cs")]
    [InlineData(@"C:\repo\", @"C:\repo\src\Core\Foo.cs", "src/Core/Foo.cs")]
    [InlineData(@"c:\repo\", @"C:\Repo\src\Core\Foo.cs", "src/Core/Foo.cs")]
    [InlineData("/repo/", "/elsewhere/src/Core/Foo.cs", "/elsewhere/src/Core/Foo.cs")]
    [InlineData(null, "/repo/src/Core/Foo.cs", "/repo/src/Core/Foo.cs")]
    public void ToRelative_NormalizesSeparatorsAndStripsRoot(string? repoRoot, string filePath, string expected)
    {
        Assert.Equal(expected, RepositoryPathNormalizer.ToRelative(filePath, repoRoot));
    }
}
