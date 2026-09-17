using System.Diagnostics.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests.Analysis;

public partial class RestrictedDependencyUseClassifierTests
{
    [Fact]
    public async Task ForbiddenMember_EveryUseReported_EvenWhenBaselined()
    {
        await AnalyzerHarness.WithBaseline(
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConstructorSite),
                SampleProjectFixture.MemberSite(SampleProjectFixture.GetUserName, SampleProjectFixture.RunSite, count: 1))
            .WithConsumer(SampleProjectFixture.ConsumerPreamble + """
                    public string Run(User user)
                    {
                        return {|BW0006:_userService.GetUserName(new Principal())|};
                    }
                }
                """)
            .RunAsync();
    }

    [Fact]
    public async Task TrackedOnlyMember_NotBaselined_ReportsNothing()
    {
        await AnalyzerHarness.WithBaseline(SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConstructorSite))
            .WithConsumer(SampleProjectFixture.ConsumerPreamble + """
                    public Guid? Run(User user)
                    {
                        return _userService.GetProperUserId(new Principal());
                    }
                }
                """)
            .RunAsync();
    }

    [Fact]
    public async Task CallThroughImplementationTypedVariable_CountsAsInterfaceMemberUse()
    {
        await AnalyzerHarness.WithBaseline(
                SampleProjectFixture.Site(DependencyUsageType.Concrete, SampleProjectFixture.RunSite))
            .WithConsumer("""
                using System.Threading.Tasks;
                using Test;

                namespace Test;

                public class Consumer
                {
                    public async Task Run(User user)
                    {
                        var service = new UserService();
                        await {|BW0006:service.CanAccessPremium(user)|};
                    }
                }
                """)
            .RunAsync();
    }

    [Fact]
    public async Task NameOf_IsNotAUse()
    {
        await AnalyzerHarness.WithBaseline(SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConstructorSite))
            .WithConsumer(SampleProjectFixture.ConsumerPreamble + """
                    public string Run(User user)
                    {
                        return nameof(_userService.CanAccessPremium);
                    }
                }
                """)
            .RunAsync();
    }

    [Fact]
    public async Task UseInsideLambda_KeysToEnclosingMethod()
    {
        await AnalyzerHarness.WithBaseline(
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConstructorSite),
                SampleProjectFixture.MemberSite(SampleProjectFixture.CanAccessPremium, SampleProjectFixture.RunSite, count: 1))
            .WithConsumer(SampleProjectFixture.ConsumerPreamble + """
                    public Func<Task<bool>> Run(User user)
                    {
                        return () => _userService.CanAccessPremium(user);
                    }
                }
                """)
            .RunAsync();
    }

    [Fact]
    public async Task MethodGroupReference_CountsAsUse()
    {
        await AnalyzerHarness.WithBaseline(SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConstructorSite))
            .WithConsumer(SampleProjectFixture.ConsumerPreamble + """
                    public Func<User, Task<bool>> Run(User user)
                    {
                        return {|BW0006:_userService.CanAccessPremium|};
                    }
                }
                """)
            .RunAsync();
    }

    [Fact]
    public async Task ImplementationCallingItsOwnSealedMember_InAllowedPath_ReportsNothing()
    {
        await AnalyzerHarness.WithBaseline()
            .WithSource("/repo/src/Core/Services/PremiumUserService.cs", """
                using System.Threading.Tasks;
                using Test;

                namespace Test;

                public class PremiumUserService : UserService
                {
                    public Task<bool> Check(User user) => CanAccessPremium(user);
                }
                """)
            .RunAsync();
    }

    private const string LambdaBody = """
            public Task<bool> Run(User user)
            {
                Func<Task<bool>> call = () => _userService.CanAccessPremium(user);
                return call();
            }
        }
        """;

    private const string LocalFunctionBody = """
            public Task<bool> Run(User user)
            {
                return Call();

                Task<bool> Call() => _userService.CanAccessPremium(user);
            }
        }
        """;

    /// <summary>
    /// Lambdas and local functions have no documentation-comment id, so a use inside one is keyed to
    /// the method that contains it and consumes that method's budget rather than falling outside
    /// every baseline row.
    /// </summary>
    [Theory]
    [InlineData(LambdaBody)]
    [InlineData(LocalFunctionBody)]
    public async Task GatedMember_UsedInsideNestedFunction_ConsumesTheContainingMethodsBudget([StringSyntax("C#-test")] string body)
    {
        await AnalyzerHarness.WithBaseline(
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConstructorSite),
                SampleProjectFixture.MemberSite(SampleProjectFixture.CanAccessPremium, SampleProjectFixture.RunSite, count: 1))
            .WithConsumer(SampleProjectFixture.ConsumerPreamble + body)
            .RunAsync();
    }

    [Fact]
    public async Task GatedMember_UsedInsideLambda_WithoutBudget_IsReportedAtTheUse()
    {
        await AnalyzerHarness.WithBaseline(SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConstructorSite))
            .WithConsumer(SampleProjectFixture.ConsumerPreamble + """
                    public Task<bool> Run(User user)
                    {
                        Func<Task<bool>> call = () => {|BW0006:_userService.CanAccessPremium(user)|};
                        return call();
                    }
                }
                """)
            .RunAsync();
    }
}
