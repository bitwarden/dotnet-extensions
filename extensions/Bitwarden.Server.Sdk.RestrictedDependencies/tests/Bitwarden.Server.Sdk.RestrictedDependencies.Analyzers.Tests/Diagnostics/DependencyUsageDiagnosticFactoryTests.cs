using Microsoft.CodeAnalysis.Testing;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests.Diagnostics;

/// <summary>
/// The wording of BW0005-BW0009 is decided in one place, so the five kinds cannot drift in which
/// argument carries what. BW0009's arguments are pinned by
/// <c>RestrictedDependencyUseClassifierTests.BaselinedServiceLocator_ReportsNoLocator_ButTheReturnTypeStillEscapes</c>,
/// where an escape is what the case is about; the other four are pinned here.
/// </summary>
public class DependencyUsageDiagnosticFactoryTests
{
    [Fact]
    public async Task Injection_NamesTheConsumerTypeTrackingAndOwner()
    {
        await new AnalyzerHarness()
            .WithBaselineJson(new BudgetModel("Test.IWidget", [], []).Serialize(), "Test.IWidget")
            .WithSource("/repo/src/Core/IWidget.cs", """
                using Bitwarden.Server.Sdk.RestrictedDependencies;

                namespace Test;

                [RestrictedDependency(Tracking = "PM-9", Owner = "team-billing")]
                public interface IWidget
                {
                }
                """)
            .WithConsumer("""
                using Test;

                namespace Test;

                public class Consumer(IWidget {|#0:widget|})
                {
                    private readonly IWidget _widget = widget;
                }
                """)
            .Expect(new DiagnosticResult(DiagnosticDescriptors.Injection)
                .WithLocation(0)
                .WithArguments("Consumer", "IWidget", "PM-9", "team-billing"))
            .RunAsync();
    }

    /// <summary>
    /// BW0006 is the one format in the family whose placeholders are out of sequence — owner comes
    /// after the hint — so the order it reads them in is worth pinning. The fixture's forbidden
    /// member carries a <c>Replacement</c>, which is the hint's longer form.
    /// </summary>
    [Fact]
    public async Task MemberUse_NamesTheMemberTypeTrackingHintAndOwner()
    {
        await AnalyzerHarness.WithBaseline(SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConstructorSite))
            .WithConsumer(SampleProjectFixture.ConsumerPreamble + """
                    public string Run(User user)
                    {
                        return {|#0:_userService.GetUserName(new Principal())|};
                    }
                }
                """)
            .Expect(new DiagnosticResult(DiagnosticDescriptors.MemberUse)
                .WithLocation(0)
                .WithArguments("GetUserName", "IUserService", "PM-1", "every use is forbidden, use IUserNameQuery instead", "unowned"))
            .RunAsync();
    }

    [Fact]
    public async Task Locator_NamesTheTypeTrackingAndOwner()
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer("""
                using System;
                using Microsoft.Extensions.DependencyInjection;
                using Test;

                namespace Test;

                public class Consumer
                {
                    public object? Resolve(IServiceProvider provider) => {|#0:provider.GetRequiredService<IUserService>()|};
                }
                """)
            .Expect(new DiagnosticResult(DiagnosticDescriptors.Locator)
                .WithLocation(0)
                .WithArguments("IUserService", "PM-1", "unowned"))
            .RunAsync();
    }

    [Fact]
    public async Task Concrete_NamesTheImplementationTypeTrackingAndOwner()
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer("""
                using Test;

                namespace Test;

                public class Consumer
                {
                    public bool Run(User user) => {|#0:UserService.IsLegacyUser(user)|};
                }
                """)
            .Expect(new DiagnosticResult(DiagnosticDescriptors.Concrete)
                .WithLocation(0)
                .WithArguments("UserService", "IUserService", "PM-1", "unowned"))
            .RunAsync();
    }
}
