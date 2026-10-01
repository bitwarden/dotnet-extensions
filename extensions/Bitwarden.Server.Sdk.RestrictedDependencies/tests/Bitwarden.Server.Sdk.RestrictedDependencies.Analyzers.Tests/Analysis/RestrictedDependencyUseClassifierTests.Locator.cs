using System.Diagnostics.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests.Analysis;

public partial class RestrictedDependencyUseClassifierTests
{
    [Theory]
    [InlineData("provider.GetRequiredService<IUserService>()")]
    [InlineData("provider.GetService<IUserService>()")]
    [InlineData("provider.GetService(typeof(IUserService))")]
    [InlineData("ActivatorUtilities.GetServiceOrCreateInstance<IUserService>(provider)")]
    [InlineData("context.RequestServices.GetRequiredService<IUserService>()")]
    public async Task ServiceLocatorForms_NotBaselined_ReportsLocator([StringSyntax("C#-test")] string resolution)
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer($$"""
                using System;
                using Microsoft.Extensions.DependencyInjection;
                using Test;

                namespace Test;

                public class RequestContext
                {
                    public IServiceProvider RequestServices { get; } = null!;
                }

                public class Consumer
                {
                    public object? Resolve(IServiceProvider provider, RequestContext context)
                    {
                        return {|BW0007:{{resolution}}|};
                    }
                }
                """)
            .RunAsync();
    }

    /// <summary>
    /// A baselined locator site reports no BW0007, but handing the resolved dependency back to the
    /// caller is a separate escape and is still reported. The two are budgeted independently.
    /// </summary>
    [Fact]
    public async Task BaselinedServiceLocator_ReportsNoLocator_ButTheReturnTypeStillEscapes()
    {
        await AnalyzerHarness.WithBaseline(
                SampleProjectFixture.Site(DependencyUsageType.Locator, "M:Test.Consumer.Resolve(System.IServiceProvider)"))
            .WithConsumer("""
                using System;
                using Microsoft.Extensions.DependencyInjection;
                using Test;

                namespace Test;

                public class Consumer
                {
                    public IUserService {|#0:Resolve|}(IServiceProvider provider)
                    {
                        return provider.GetRequiredService<IUserService>();
                    }
                }
                """)
            .Expect(new Microsoft.CodeAnalysis.Testing.DiagnosticResult(DiagnosticDescriptors.Escape)
                .WithLocation(0)
                .WithArguments("IUserService", "PM-1", "Resolve", "unowned"))
            .RunAsync();
    }

    /// <summary>
    /// The negative control for the locator forms above: a method that merely shares a name with
    /// one of them, on a receiver that is not a service provider, resolves nothing from a
    /// container. BW0007 is a non-configurable error, so a false positive here can only be got
    /// past by recording debt that does not exist.
    /// </summary>
    [Fact]
    public async Task LocatorNamedMethod_OnAnUnrelatedReceiver_ReportsNothing()
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer("""
                using Test;

                namespace Test;

                public static class MyOwnFactory
                {
                    public static T CreateInstance<T>() => default!;
                }

                public class Consumer
                {
                    public object? Resolve() => MyOwnFactory.CreateInstance<IUserService>();
                }
                """)
            .RunAsync();
    }

    [Fact]
    public async Task ServiceLocator_ForUnrestrictedType_ReportsNothing()
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer("""
                using System;
                using Microsoft.Extensions.DependencyInjection;
                using Test;

                namespace Test;

                public class Consumer
                {
                    public User Resolve(IServiceProvider provider)
                    {
                        return provider.GetRequiredService<User>();
                    }
                }
                """)
            .RunAsync();
    }
}
