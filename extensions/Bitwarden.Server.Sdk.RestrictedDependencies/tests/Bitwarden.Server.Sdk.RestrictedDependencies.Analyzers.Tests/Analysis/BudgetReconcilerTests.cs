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

    private const string OtherConsumerSite = "T:Test.OtherConsumer";

    private const string DecoratorSite = "T:Test.Decorator";

    [Fact]
    public async Task BudgetIsPerType_OneTypeCannotSpendAnothersAllowance()
    {
        // Site is part of the key BudgetFor filters on. Dropping it would pool both rows into a
        // budget of two for the whole restricted type and let a use move between types unreported.
        await AnalyzerHarness.WithBaseline(
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConsumerSite),
                SampleProjectFixture.Site(DependencyUsageType.Injection, OtherConsumerSite),
                SampleProjectFixture.MemberSite(SampleProjectFixture.CanAccessPremium, SampleProjectFixture.ConsumerSite, count: 1),
                SampleProjectFixture.MemberSite(SampleProjectFixture.CanAccessPremium, OtherConsumerSite, count: 1))
            .WithConsumer(SampleProjectFixture.ConsumerPreamble + """
                    public async Task Run(User user)
                    {
                        await _userService.CanAccessPremium(user);
                        await {|BW0006:_userService.CanAccessPremium(user)|};
                    }
                }

                public class OtherConsumer
                {
                    private readonly IUserService _userService;

                    public OtherConsumer(IUserService userService)
                    {
                        _userService = userService;
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
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConsumerSite),
                SampleProjectFixture.MemberSite(SampleProjectFixture.CanAccessPremium, SampleProjectFixture.ConsumerSite, count: 1))
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
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConsumerSite),
                SampleProjectFixture.MemberSite(SampleProjectFixture.CanAccessPremium, SampleProjectFixture.ConsumerSite, count: 2))
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
    public async Task SurplusUse_SharingItsKey_NamesItsOwnSubject()
    {
        // Escape is keyed by the containing type, so a decorator's return type and parameter
        // share one key. The surplus is the parameter, and the message must name it rather than
        // whichever use opened the record.
        await AnalyzerHarness.WithBaseline(SampleProjectFixture.Site(DependencyUsageType.Escape, DecoratorSite))
            .WithConsumer("""
                using Test;

                namespace Test;

                public class Decorator
                {
                    public IUserService Wrap(IUserService {|#0:inner|}) => inner;
                }
                """)
            .Expect(new DiagnosticResult(DiagnosticDescriptors.Escape)
                .WithLocation(0)
                .WithArguments("IUserService", "PM-1", "inner", "unowned"))
            .RunAsync();
    }

    [Fact]
    public async Task StaleEntry_SiteNoLongerExists_ReportsWithoutLocation()
    {
        await AnalyzerHarness.WithBaseline(
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConsumerSite),
                SampleProjectFixture.MemberSite(SampleProjectFixture.CanAccessPremium, SampleProjectFixture.ConsumerSite),
                SampleProjectFixture.Site(DependencyUsageType.Injection, "T:Test.Gone"))
            .WithConsumer(SampleProjectFixture.Consumer)
            .Expect(new DiagnosticResult(DiagnosticDescriptors.StaleBaseline)
                .WithArguments(SampleProjectFixture.Type, 1, "injection use", "T:Test.Gone", SampleProjectFixture.Project, 0, UpdateInstruction))
            .RunAsync();
    }

    [Fact]
    public async Task StaleEntry_CountHigherThanCode_ReportsAtTheSite()
    {
        await AnalyzerHarness.WithBaseline(
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConsumerSite),
                SampleProjectFixture.MemberSite(SampleProjectFixture.CanAccessPremium, SampleProjectFixture.ConsumerSite, count: 2))
            .WithConsumer(SampleProjectFixture.Consumer.Replace("public class Consumer", "public class {|#0:Consumer|}"))
            .Expect(new DiagnosticResult(DiagnosticDescriptors.StaleBaseline)
                .WithLocation(0)
                .WithArguments(SampleProjectFixture.Type, 2, $"use(s) of '{SampleProjectFixture.CanAccessPremium}'", SampleProjectFixture.ConsumerSite, SampleProjectFixture.Project, 1, UpdateInstruction))
            .RunAsync();
    }

    /// <summary>
    /// A member row keeps the restricted member's full id, so a signature change leaves the old row
    /// unspent and the call uncovered. The seal is off so that only the reconciler reports.
    /// </summary>
    [Fact]
    public async Task GatedMember_SignatureChanged_ReportsUseAndStaleRow()
    {
        await new AnalyzerHarness()
            .WithSource(SampleProjectFixture.ServicePath, SampleProjectFixture.Service
                .Replace("SealMembers = true, ", string.Empty)
                .Replace("Task<bool> CanAccessPremium(User user);", "Task<bool> CanAccessPremium(User user, bool strict = false);")
                .Replace("public Task<bool> CanAccessPremium(User user) =>", "public Task<bool> CanAccessPremium(User user, bool strict = false) =>"))
            .WithBaselineJson(SampleProjectFixture.Baseline(
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConsumerSite),
                SampleProjectFixture.MemberSite(SampleProjectFixture.CanAccessPremium, SampleProjectFixture.ConsumerSite)))
            .WithConsumer(SampleProjectFixture.Consumer
                .Replace("public class Consumer", "public class {|#0:Consumer|}")
                .Replace("=> _userService.CanAccessPremium(user);", "=> {|BW0006:_userService.CanAccessPremium(user)|};"))
            .Expect(new DiagnosticResult(DiagnosticDescriptors.StaleBaseline)
                .WithLocation(0)
                .WithArguments(SampleProjectFixture.Type, 1, $"use(s) of '{SampleProjectFixture.CanAccessPremium}'", SampleProjectFixture.ConsumerSite, SampleProjectFixture.Project, 0, UpdateInstruction))
            .RunAsync();
    }

    [Fact]
    public async Task StaleEntry_ForTrackedOnlyMember_IsNotReported()
    {
        await AnalyzerHarness.WithBaseline(
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConsumerSite),
                SampleProjectFixture.MemberSite(SampleProjectFixture.CanAccessPremium, SampleProjectFixture.ConsumerSite),
                SampleProjectFixture.MemberSite(SampleProjectFixture.GetProperUserId, SampleProjectFixture.ConsumerSite, count: 5))
            .WithConsumer(SampleProjectFixture.Consumer)
            .RunAsync();
    }
}
