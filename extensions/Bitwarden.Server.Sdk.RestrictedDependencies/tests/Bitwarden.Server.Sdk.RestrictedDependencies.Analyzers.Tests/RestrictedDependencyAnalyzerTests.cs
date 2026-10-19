namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests;

/// <summary>
/// What the analyzer decides before any collaborator runs: whether the project is in scope.
/// </summary>
public class RestrictedDependencyAnalyzerTests
{
    [Fact]
    public async Task OutOfScopeProject_ReportsNothing()
    {
        await new AnalyzerHarness(enabled: false)
            .WithService()
            .WithConsumer(SampleProjectFixture.Consumer)
            .RunAsync();
    }
}
