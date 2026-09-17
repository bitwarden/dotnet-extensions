using System.Diagnostics.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests.Analysis;

public partial class RestrictedDependencyUseClassifierTests
{
    private const string BaseConstructorSite = "M:Test.BaseConsumer.#ctor(Test.IUserService)";

    private const string NonConstructorParameter = """
        public class Consumer
        {
            public void Run(IUserService {|BW0009:userService|})
            {
            }
        }
        """;

    private const string ReturnType = """
        public class Consumer
        {
            public IUserService {|BW0009:Run|}() => null!;
        }
        """;

    private const string DelegateType = "public delegate void UserServiceHandler(IUserService {|BW0009:userService|});";

    private const string LazyConstructorParameter = """
        public class Consumer
        {
            private readonly Lazy<IUserService> _userService;

            public Consumer(Lazy<IUserService> {|BW0009:userService|})
            {
                _userService = userService;
            }
        }
        """;

    private const string ProtectedProperty = """
        public abstract class BaseConsumer
        {
            protected IUserService {|BW0009:UserService|} { get; }

            protected BaseConsumer(IUserService userService)
            {
                UserService = userService;
            }
        }
        """;

    private const string PublicField = """
        public class Consumer
        {
            public IUserService {|BW0009:UserService|};
        }
        """;

    private const string PublicEvent = """
        public class Consumer
        {
            public event Action<IUserService> {|BW0009:Resolved|};
        }
        """;

    private const string ArrayParameter = """
        public class Consumer
        {
            public void Run(IUserService[] {|BW0009:userServices|})
            {
            }
        }
        """;

    private const string ConstrainedMethodParameter = """
        public class Consumer
        {
            public void Run<T>(T {|BW0009:userService|}) where T : IUserService
            {
            }
        }
        """;

    private const string ConstrainedTypeParameterInsideLazy = """
        public class Consumer<T> where T : IUserService
        {
            public Consumer(Lazy<T> {|BW0009:userService|})
            {
            }
        }
        """;

    /// <summary>
    /// A <c>Lazy&lt;T&gt;</c> parameter and a protected property are escapes rather than
    /// injections, so the constructor that receives the dependency needs its own baseline row
    /// wherever one exists; otherwise the injection would be reported too.
    /// </summary>
    [Theory]
    [InlineData(NonConstructorParameter, null)]
    [InlineData(ReturnType, null)]
    [InlineData(DelegateType, null)]
    [InlineData(LazyConstructorParameter, null)]
    [InlineData(ProtectedProperty, BaseConstructorSite)]
    [InlineData(PublicField, null)]
    [InlineData(PublicEvent, null)]
    [InlineData(ArrayParameter, null)]
    [InlineData(ConstrainedMethodParameter, null)]
    [InlineData(ConstrainedTypeParameterInsideLazy, null)]
    public async Task RestrictedTypeLeavingItsConsumer_ReportsEscape(
        [StringSyntax("C#-test")] string declaration,
        string? baselinedConstructor)
    {
        BudgetEntry[] baseline = baselinedConstructor is null
            ? []
            : [SampleProjectFixture.Site(DependencyUsageType.Injection, baselinedConstructor)];

        await AnalyzerHarness.WithBaseline(baseline)
            .WithConsumer($$"""
                using System;
                using Test;

                namespace Test;

                {{declaration}}
                """)
            .RunAsync();
    }

    /// <summary>
    /// The negative control for <see cref="PublicField"/>: the same field, made private, is
    /// storage rather than a way out of the class, so it earns no BW0009 of its own and the
    /// baselined constructor injection is the only row the consumer needs.
    /// </summary>
    [Fact]
    public async Task PrivateBackingField_IsStorageNotEscape()
    {
        await AnalyzerHarness.WithBaseline(SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConstructorSite))
            .WithConsumer(SampleProjectFixture.ConsumerPreamble + "}")
            .RunAsync();
    }

    [Fact]
    public async Task BaseConstructorPassThrough_ReportsEscapeAtArgument()
    {
        await AnalyzerHarness.WithBaseline(
                SampleProjectFixture.Site(DependencyUsageType.Injection, BaseConstructorSite),
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConstructorSite))
            .WithConsumer("""
                using Test;

                namespace Test;

                public abstract class BaseConsumer
                {
                    private readonly IUserService _userService;

                    protected BaseConsumer(IUserService userService)
                    {
                        _userService = userService;
                    }
                }

                public class Consumer : BaseConsumer
                {
                    public Consumer(IUserService userService) : base({|BW0009:userService|})
                    {
                    }
                }
                """)
            .RunAsync();
    }

    /// <summary>
    /// The negative control for <see cref="BaseConstructorPassThrough_ReportsEscapeAtArgument"/>.
    /// Roslyn models `: base(...)` and `: this(...)` the same way, but a `this(...)` chain hands
    /// the dependency to no one: the constructor it chains to already counts the parameter as
    /// injection, so counting the argument too would demand a baseline row for a use that is the
    /// same one twice.
    /// </summary>
    [Fact]
    public async Task ThisConstructorChain_IsNotAnEscape()
    {
        await AnalyzerHarness.WithBaseline(
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConstructorSite),
                SampleProjectFixture.Site(DependencyUsageType.Injection, "M:Test.Consumer.#ctor(Test.IUserService,System.Int32)"))
            .WithConsumer("""
                using Test;

                namespace Test;

                public class Consumer
                {
                    private readonly IUserService _userService;

                    public Consumer(IUserService userService) : this(userService, 0)
                    {
                    }

                    public Consumer(IUserService userService, int retries)
                    {
                        _userService = userService;
                    }
                }
                """)
            .RunAsync();
    }

    /// <summary>
    /// Following a type parameter's constraints makes the walk cyclic, and
    /// <c>T : IEquatable&lt;T&gt;</c> is the ordinary shape that closes the loop. It must
    /// terminate rather than recurse until the stack goes.
    /// </summary>
    [Fact]
    public async Task SelfReferentialConstraint_Terminates()
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer("""
                using System;
                using Test;

                namespace Test;

                public class Consumer
                {
                    public void Run<T>(Lazy<T> value) where T : IEquatable<T>
                    {
                    }
                }
                """)
            .RunAsync();
    }
}
