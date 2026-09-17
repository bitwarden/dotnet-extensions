using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Rules;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests.Rules;

public class PathGlobMatcherTests
{
    [Theory]
    [InlineData("src/Core/Services/**", "src/Core/Services/UserService.cs", true)]
    [InlineData("src/Core/Services/**", "src/Core/Services/Nested/Deep.cs", true)]
    [InlineData("src/Core/Services/**", "src/Core/ServicesX/UserService.cs", false)]
    [InlineData("**/AdminConsole/**", "src/Core/AdminConsole/Services/OrganizationService.cs", true)]
    [InlineData("**/AdminConsole/**", "bitwarden_license/src/Commercial.Core/AdminConsole/Foo.cs", true)]
    [InlineData("**/AdminConsole/**", "src/Core/Vault/Foo.cs", false)]
    [InlineData("src/Infrastructure.*/**", "src/Infrastructure.Dapper/Repositories/OrganizationRepository.cs", true)]
    [InlineData("src/Infrastructure.*/**", "src/Infrastructure/Foo.cs", false)]
    [InlineData("src/Core/Auth/*.cs", "src/Core/Auth/Foo.cs", true)]
    [InlineData("src/Core/Auth/*.cs", "src/Core/Auth/Sub/Foo.cs", false)]
    public void IsMatch_FollowsCodeownersSemantics(string pattern, string path, bool expected)
    {
        Assert.True(PathGlobMatcher.TryCreate(pattern, out var glob));
        Assert.Equal(expected, glob!.IsMatch(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"src\Core\**")]
    [InlineData("/src/Core/**")]
    [InlineData("src//Core/**")]
    public void TryCreate_RejectsNonRepoRelativeForms(string pattern)
    {
        Assert.False(PathGlobMatcher.TryCreate(pattern, out var glob));
        Assert.Null(glob);
    }
}
