using System.Diagnostics.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests.Analysis;

/// <summary>
/// BW0010, BW0011, and what an exception covers. A valid exception on a class covers the
/// injection sites inside it; one on a member covers member uses inside that member and its
/// accessors. An invalid exception excepts nothing, and an expired one warns but still excepts.
/// </summary>
public class DependencyExceptionTrackerTests
{
    [Fact]
    public async Task ValidClassLevelException_CoversInjectionSite()
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer($$"""
                using Bitwarden.Server.Sdk.RestrictedDependencies;
                using Test;

                namespace Test;

                {{SampleProjectFixture.ValidException}}
                public class Consumer
                {
                    public Consumer(IUserService userService)
                    {
                    }
                }
                """)
            .RunAsync();
    }

    [Fact]
    public async Task ValidMemberLevelException_CoversMemberUsesInThatMemberOnly()
    {
        await AnalyzerHarness.WithBaseline(SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConstructorSite))
            .WithConsumer($$"""
                using System.Threading.Tasks;
                using Bitwarden.Server.Sdk.RestrictedDependencies;
                using Test;

                namespace Test;

                public class Consumer
                {
                    private readonly IUserService _userService;

                    public Consumer(IUserService userService)
                    {
                        _userService = userService;
                    }

                    {{SampleProjectFixture.ValidException}}
                    public Task<bool> Excepted(User user) => _userService.CanAccessPremium(user);

                    public Task<bool> NotExcepted(User user) => {|BW0006:_userService.CanAccessPremium(user)|};
                }
                """)
            .RunAsync();
    }

    private const string ExpressionBodiedProperty = SampleProjectFixture.ValidException + """

        public Task<bool> Excepted => _userService.CanAccessPremium(new User());
        """;

    private const string BlockBodiedGetter = SampleProjectFixture.ValidException + """

        public Task<bool> Excepted
        {
            get { return _userService.CanAccessPremium(new User()); }
        }
        """;

    private const string EventAddAccessor = SampleProjectFixture.ValidException + """

        public event EventHandler Excepted
        {
            add { _userService.CanAccessPremium(new User()); }
            remove { }
        }
        """;

    [Theory]
    [InlineData(ExpressionBodiedProperty)]
    [InlineData(BlockBodiedGetter)]
    [InlineData(EventAddAccessor)]
    public async Task ValidPropertyOrEventLevelException_CoversMemberUsesInItsAccessors([StringSyntax("C#-test")] string excepted)
    {
        await AnalyzerHarness.WithBaseline(SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConstructorSite))
            .WithConsumer($$"""
                using System;
                using System.Threading.Tasks;
                using Bitwarden.Server.Sdk.RestrictedDependencies;
                using Test;

                namespace Test;

                public class Consumer
                {
                    private readonly IUserService _userService;

                    public Consumer(IUserService userService)
                    {
                        _userService = userService;
                    }

                    {{excepted}}

                    public Task<bool> NotExcepted => {|BW0006:_userService.CanAccessPremium(new User())|};
                }
                """)
            .RunAsync();
    }

    [Fact]
    public async Task IncompleteException_ReportsInvalidAndDoesNotExcept()
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer("""
                using Bitwarden.Server.Sdk.RestrictedDependencies;
                using Test;

                namespace Test;

                [{|BW0010:RestrictedDependencyException(typeof(IUserService), Owner = "team-billing")|}]
                public class Consumer
                {
                    public Consumer(IUserService {|BW0005:userService|})
                    {
                    }
                }
                """)
            .RunAsync();
    }

    [Fact]
    public async Task MalformedExpiryDate_ReportsInvalid()
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer("""
                using Bitwarden.Server.Sdk.RestrictedDependencies;
                using Test;

                namespace Test;

                [{|BW0010:RestrictedDependencyException(typeof(IUserService), Owner = "team-billing", Reason = "PM-1", Expires = "next quarter")|}]
                public class Consumer
                {
                }
                """)
            .RunAsync();
    }

    [Fact]
    public async Task ExpiredException_WarnsAndStillExcepts()
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer("""
                using Bitwarden.Server.Sdk.RestrictedDependencies;
                using Test;

                namespace Test;

                [{|BW0011:RestrictedDependencyException(typeof(IUserService), Owner = "team-billing", Reason = "PM-12345", Expires = "2020-01-01")|}]
                public class Consumer
                {
                    public Consumer(IUserService userService)
                    {
                    }
                }
                """)
            .RunAsync();
    }

    private const string MissingOwner = """
        [{|BW0010:RestrictedDependencyException(typeof(IUserService), Reason = "PM-1", Expires = "2099-12-31")|}]
        public class Consumer
        {
        }
        """;

    private const string RestrictedTypeNotATypeOf = """
        [{|BW0010:RestrictedDependencyException(null, Owner = "team-billing", Reason = "PM-1", Expires = "2099-12-31")|}]
        public class Consumer
        {
        }
        """;

    [Theory]
    [InlineData(MissingOwner)]
    [InlineData(RestrictedTypeNotATypeOf)]
    public async Task MissingRequiredPart_ReportsInvalid([StringSyntax("C#-test")] string declaration)
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer($$"""
                using Bitwarden.Server.Sdk.RestrictedDependencies;
                using Test;

                namespace Test;

                {{declaration}}
                """)
            .RunAsync();
    }
}
