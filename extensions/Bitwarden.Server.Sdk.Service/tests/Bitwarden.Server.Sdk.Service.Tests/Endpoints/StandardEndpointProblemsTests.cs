using Bitwarden.Server.Sdk.Service.Endpoints;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// Problems found while endpoints are built are reported once each, in the order found, however
/// many times the framework builds an endpoint and from however many threads.
/// </summary>
public sealed class StandardEndpointProblemsTests
{
    [Fact]
    public void All_Nothing_Empty()
    {
        Assert.Empty(new StandardEndpointProblems().All);
    }

    [Fact]
    public void All_SameProblemTwice_OnceInTheOrderFound()
    {
        var problems = new StandardEndpointProblems();

        problems.Add("second");
        problems.Add("first");
        problems.Add("second");

        Assert.Equal(["second", "first"], problems.All);
    }

    // Ordinal, so problems differing only in case are both reported.
    [Fact]
    public void All_DifferingInCase_Both()
    {
        var problems = new StandardEndpointProblems();

        problems.Add("Problem");
        problems.Add("problem");

        Assert.Equal(2, problems.All.Count);
    }

    [Fact]
    public void All_IsASnapshot()
    {
        var problems = new StandardEndpointProblems();
        problems.Add("one");

        var snapshot = problems.All;
        problems.Add("two");

        Assert.Equal(["one"], snapshot);
    }

    [Fact]
    public async Task Add_FromManyThreads_EachOnce()
    {
        var problems = new StandardEndpointProblems();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(
            () =>
            {
                for (var i = 0; i < 200; i++)
                {
                    problems.Add($"problem {i}");
                }
            },
            TestContext.Current.CancellationToken)));

        Assert.Equal(200, problems.All.Count);
        Assert.Equal(200, problems.All.Distinct().Count());
    }
}
