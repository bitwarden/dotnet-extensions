using System.Diagnostics.CodeAnalysis;
using Bitwarden.Server.Sdk.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.CodeFixers.Tests;

public class DependencyInjectionCodeFixerTests : CSharpCodeFixTest<DependencyInjectionAnalyzer, DependencyInjectionCodeFixer, DefaultVerifier>
{
    public DependencyInjectionCodeFixerTests()
    {
        TestState.OutputKind = OutputKind.ConsoleApplication;
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location));
        var common = ("Service.cs",
            """
            using System;

            namespace Test;

            public interface IMyService
            {
                void Run();
            }

            public class MyService : IMyService
            {
                public void Run() { }
            }
            """
        );
        TestState.Sources.Add(common);
        FixedState.Sources.Add(common);
    }

    [Theory]
    [InlineData("Singleton", "")]
    [InlineData("Scoped", "")]
    [InlineData("Transient", "")]
    [InlineData("KeyedSingleton", "\"test\"")]
    [InlineData("KeyedScoped", "\"test\"")]
    [InlineData("KeyedTransient", "\"test\"")]
    public async Task BothServiceAndImplementationAsGenerics(string lifetime, string key)
    {
        await RunCodeFixAsync($$"""
            using Test;
            using Microsoft.Extensions.DependencyInjection;

            var services = new ServiceCollection();
            {|BW0003:services.Add{{lifetime}}<IMyService, MyService>({{key}})|};
            """,
            $$"""
            using Test;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            var services = new ServiceCollection();
            services.TryAdd{{lifetime}}<IMyService, MyService>({{key}});
            """
        );
    }

    [Theory]
    [InlineData("Singleton", "")]
    [InlineData("Scoped", "")]
    [InlineData("Transient", "")]
    [InlineData("KeyedSingleton", "\"test\"")]
    [InlineData("KeyedScoped", "\"test\"")]
    [InlineData("KeyedTransient", "\"test\"")]
    public async Task BothServiceAndImplementationAsTypeofArguments(string lifetime, string key)
    {
        var arguments = key == ""
            ? $"typeof(MyService)"
            : $"{key}, typeof(MyService)";

        await RunCodeFixAsync($$"""
            using Test;
            using Microsoft.Extensions.DependencyInjection;

            var services = new ServiceCollection();
            {|BW0003:services.Add{{lifetime}}(typeof(IMyService), {{arguments}})|};
            """,
            $$"""
            using Test;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            var services = new ServiceCollection();
            services.TryAdd{{lifetime}}(typeof(IMyService), {{arguments}});
            """
        );
    }

    [Theory]
    [InlineData("Singleton", "")]
    [InlineData("Scoped", "")]
    [InlineData("Transient", "")]
    [InlineData("KeyedSingleton", "\"test\"")]
    [InlineData("KeyedScoped", "\"test\"")]
    [InlineData("KeyedTransient", "\"test\"")]
    public async Task ServiceAsGenericAndImplementationFactory(string lifetime, string key)
    {
        var arguments = key == ""
            ? $"(sp) => new MyService()"
            : $"{key}, (sp, key) => new MyService()";

        await RunCodeFixAsync($$"""
            using Test;
            using Microsoft.Extensions.DependencyInjection;

            var services = new ServiceCollection();
            {|BW0003:services.Add{{lifetime}}<IMyService>({{arguments}})|};
            """,
            $$"""
            using Test;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            var services = new ServiceCollection();
            services.TryAdd{{lifetime}}<IMyService>({{arguments}});
            """
        );
    }

    [Fact]
    public async Task UsedInMethod()
    {
        TestState.OutputKind = OutputKind.DynamicallyLinkedLibrary;

        await RunCodeFixAsync("""
            using Test;
            using Microsoft.Extensions.DependencyInjection;

            public static class Extensions
            {
                public static IServiceCollection AddServices(this IServiceCollection services)
                {
                    {|BW0003:services.AddTransient<IMyService, MyService>()|};
                    return services;
                }
            }
            """,
            """
            using Test;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public static class Extensions
            {
                public static IServiceCollection AddServices(this IServiceCollection services)
                {
                    services.TryAddTransient<IMyService, MyService>();
                    return services;
                }
            }
            """
        );
    }

    private async Task RunCodeFixAsync([StringSyntax("C#-test")] string inputSource, [StringSyntax("C#-test")] string expectedFixedSource)
    {
        TestCode = inputSource;
        FixedCode = expectedFixedSource;

        TestState.ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location));

        await RunAsync(TestContext.Current.CancellationToken);
    }
}
