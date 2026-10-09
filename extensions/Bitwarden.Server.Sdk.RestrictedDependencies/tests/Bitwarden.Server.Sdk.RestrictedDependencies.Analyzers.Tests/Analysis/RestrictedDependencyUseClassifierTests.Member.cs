using System.Diagnostics.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests.Analysis;

public partial class RestrictedDependencyUseClassifierTests
{
    [Fact]
    public async Task ForbiddenMember_EveryUseReported_EvenWhenBaselined()
    {
        await AnalyzerHarness.WithBaseline(
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConsumerSite),
                SampleProjectFixture.MemberSite(SampleProjectFixture.GetUserName, SampleProjectFixture.ConsumerSite, count: 1))
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
        await AnalyzerHarness.WithBaseline(SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConsumerSite))
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
                SampleProjectFixture.Site(DependencyUsageType.Concrete, SampleProjectFixture.ConsumerSite))
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
        await AnalyzerHarness.WithBaseline(SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConsumerSite))
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
    public async Task MethodGroupReference_CountsAsUse()
    {
        await AnalyzerHarness.WithBaseline(SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConsumerSite))
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

    private const string ParameterAdded = """
            public Task<bool> Run(User user, bool force) => _userService.CanAccessPremium(user);
        }
        """;

    private const string MethodRenamed = """
            public Task<bool> Execute(User user) => _userService.CanAccessPremium(user);
        }
        """;

    private const string UseMovedToAnotherMethod = """
            public Task<bool> Run(User user) => Check(user);

            private Task<bool> Check(User user) => _userService.CanAccessPremium(user);
        }
        """;

    /// <summary>
    /// A member row names the type, so changing the parameters or name of the method that holds the
    /// use, or moving the use to another method of the same type, leaves the row where it was.
    /// </summary>
    [Theory]
    [InlineData(ParameterAdded)]
    [InlineData(MethodRenamed)]
    [InlineData(UseMovedToAnotherMethod)]
    public async Task GatedMember_ReshapedMethod_ConsumesTheTypesBudget([StringSyntax("C#-test")] string body)
    {
        await AnalyzerHarness.WithBaseline(
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConsumerSite),
                SampleProjectFixture.MemberSite(SampleProjectFixture.CanAccessPremium, SampleProjectFixture.ConsumerSite, count: 1))
            .WithConsumer(SampleProjectFixture.ConsumerPreamble + body)
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

    private const string LinqSelect = """
            public IEnumerable<Task<bool>> Run(IEnumerable<User> users) => users.Select(u => _userService.CanAccessPremium(u));
        }
        """;

    private const string QuerySyntax = """
            public IEnumerable<Task<bool>> Run(IEnumerable<User> users) => from u in users select _userService.CanAccessPremium(u);
        }
        """;

    private const string AnonymousMethod = """
            public Task<bool> Run(User user)
            {
                Func<User, Task<bool>> call = delegate (User u) { return _userService.CanAccessPremium(u); };
                return call(user);
            }
        }
        """;

    private const string ExpressionTree = """
            public Expression<Func<User, Task<bool>>> Run()
            {
                Expression<Func<User, Task<bool>>> e = u => _userService.CanAccessPremium(u);
                return e;
            }
        }
        """;

    private const string ExpressionBodiedPropertyLambda = """
            public Func<User, Task<bool>> Check => u => _userService.CanAccessPremium(u);
        }
        """;

    /// <summary>
    /// A use inside a lambda, local function, anonymous method or query clause is keyed to the type
    /// that contains it, like any other use. A budget of one passes only when the use is counted
    /// exactly once: none would leave the row stale and two would exceed it.
    /// </summary>
    [Theory]
    [InlineData(LambdaBody)]
    [InlineData(LocalFunctionBody)]
    [InlineData(LinqSelect)]
    [InlineData(QuerySyntax)]
    [InlineData(AnonymousMethod)]
    [InlineData(ExpressionTree)]
    [InlineData(ExpressionBodiedPropertyLambda)]
    public async Task GatedMember_UsedInsideNestedFunction_ConsumesTheContainingTypesBudget([StringSyntax("C#-test")] string body)
    {
        await AnalyzerHarness.WithBaseline(
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConsumerSite),
                SampleProjectFixture.MemberSite(SampleProjectFixture.CanAccessPremium, SampleProjectFixture.ConsumerSite, count: 1))
            .WithConsumer("""
                using System.Collections.Generic;
                using System.Linq;
                using System.Linq.Expressions;

                """ + SampleProjectFixture.ConsumerPreamble + body)
            .RunAsync();
    }

    [Fact]
    public async Task GatedMember_ReachedThroughALambdaParameter_IsAMemberUse()
    {
        await AnalyzerHarness.WithBaseline(
                SampleProjectFixture.MemberSite(SampleProjectFixture.CanAccessPremium, SampleProjectFixture.ConsumerSite, count: 1),
                SampleProjectFixture.Site(DependencyUsageType.Escape, "T:Test.Account"))
            .WithConsumer("""
                using System.Collections.Generic;
                using System.Linq;
                using System.Threading.Tasks;
                using Test;

                namespace Test;

                public class Account
                {
                    public IUserService UserService { get; init; } = null!;
                }

                public class Consumer
                {
                    public IEnumerable<Task<bool>> Run(IEnumerable<Account> accounts, User user) =>
                        accounts.Select(a => a.UserService.CanAccessPremium(user));
                }
                """)
            .RunAsync();
    }

    [Fact]
    public async Task GatedMember_UsedInsideLambda_WithoutBudget_IsReportedAtTheUse()
    {
        await AnalyzerHarness.WithBaseline(SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConsumerSite))
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
