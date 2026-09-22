using Microsoft.CodeAnalysis.Testing;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests.Analysis;

/// <summary>
/// The reconciler spends a row's budget as a sum keyed by site, then reports what the code no
/// longer uses one row at a time. A use beyond its key's budget is BW0006 at the surplus site;
/// budget the code no longer spends is BW0013.
/// </summary>
public class BudgetReconcilerTests
{
    /// <summary>
    /// What BW0013 tells the reader to run when the build configures no regenerate command.
    /// </summary>
    private const string UpdateInstruction = "regenerate the committed baselines";

    private const string OtherSite = "M:Test.Consumer.Other(Test.User)";

    [Fact]
    public async Task BudgetIsPerSite_OneSiteCannotSpendAnothersAllowance()
    {
        // Site is part of the key BudgetFor filters on. Dropping it would pool both rows into a
        // budget of two for the whole type and let a use move between methods unreported.
        await AnalyzerHarness.WithBaseline(
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConstructorSite),
                SampleProjectFixture.MemberSite(SampleProjectFixture.CanAccessPremium, SampleProjectFixture.RunSite, count: 1),
                SampleProjectFixture.MemberSite(SampleProjectFixture.CanAccessPremium, OtherSite, count: 1))
            .WithConsumer(SampleProjectFixture.ConsumerPreamble + """
                    public async Task Run(User user)
                    {
                        await _userService.CanAccessPremium(user);
                        await {|BW0006:_userService.CanAccessPremium(user)|};
                    }

                    public async Task Other(User user)
                    {
                        await _userService.CanAccessPremium(user);
                    }
                }
                """)
            .RunAsync();
    }

    [Fact]
    public async Task GatedMember_OverBaselineCount_ReportsOnlyTheExcessUse()
    {
        await AnalyzerHarness.WithBaseline(
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConstructorSite),
                SampleProjectFixture.MemberSite(SampleProjectFixture.CanAccessPremium, SampleProjectFixture.RunSite, count: 1))
            .WithConsumer(SampleProjectFixture.ConsumerPreamble + """
                    public async Task Run(User user)
                    {
                        await _userService.CanAccessPremium(user);
                        await {|BW0006:_userService.CanAccessPremium(user)|};
                    }
                }
                """)
            .RunAsync();
    }

    [Fact]
    public async Task GatedMember_WithinBaselineCount_ReportsNothing()
    {
        await AnalyzerHarness.WithBaseline(
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConstructorSite),
                SampleProjectFixture.MemberSite(SampleProjectFixture.CanAccessPremium, SampleProjectFixture.RunSite, count: 2))
            .WithConsumer(SampleProjectFixture.ConsumerPreamble + """
                    public async Task Run(User user)
                    {
                        await _userService.CanAccessPremium(user);
                        await _userService.CanAccessPremium(user);
                    }
                }
                """)
            .RunAsync();
    }

    [Fact]
    public async Task StaleEntry_SiteNoLongerExists_ReportsWithoutLocation()
    {
        await AnalyzerHarness.WithBaseline(
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConstructorSite),
                SampleProjectFixture.MemberSite(SampleProjectFixture.CanAccessPremium, SampleProjectFixture.RunSite),
                SampleProjectFixture.Site(DependencyUsageType.Injection, "M:Test.Gone.#ctor(Test.IUserService)"))
            .WithConsumer(SampleProjectFixture.Consumer)
            .Expect(new DiagnosticResult(DiagnosticDescriptors.StaleBaseline)
                .WithArguments(SampleProjectFixture.Type, 1, "injection use", "M:Test.Gone.#ctor(Test.IUserService)", SampleProjectFixture.Project, 0, UpdateInstruction))
            .RunAsync();
    }

    [Fact]
    public async Task StaleEntry_CountHigherThanCode_ReportsAtTheSite()
    {
        await AnalyzerHarness.WithBaseline(
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConstructorSite),
                SampleProjectFixture.MemberSite(SampleProjectFixture.CanAccessPremium, SampleProjectFixture.RunSite, count: 2))
            .WithConsumer(SampleProjectFixture.Consumer.Replace("Run(User user)", "{|#0:Run|}(User user)"))
            .Expect(new DiagnosticResult(DiagnosticDescriptors.StaleBaseline)
                .WithLocation(0)
                .WithArguments(SampleProjectFixture.Type, 2, $"use(s) of '{SampleProjectFixture.CanAccessPremium}'", SampleProjectFixture.RunSite, SampleProjectFixture.Project, 1, UpdateInstruction))
            .RunAsync();
    }

    [Fact]
    public async Task StaleEntry_ForTrackedOnlyMember_IsNotReported()
    {
        await AnalyzerHarness.WithBaseline(
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConstructorSite),
                SampleProjectFixture.MemberSite(SampleProjectFixture.CanAccessPremium, SampleProjectFixture.RunSite),
                SampleProjectFixture.MemberSite(SampleProjectFixture.GetProperUserId, SampleProjectFixture.RunSite, count: 5))
            .WithConsumer(SampleProjectFixture.Consumer)
            .RunAsync();
    }
}
