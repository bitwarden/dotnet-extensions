using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Analyzers.Tests;

public class DependencyInjectionAnalyzerTests : AnalyzerTests<DependencyInjectionAnalyzer>
{
    public DependencyInjectionAnalyzerTests()
    {
        TestState.OutputKind = OutputKind.ConsoleApplication;
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location));
        TestState.Sources.Add(("Service.cs", """
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
            """)
        );
    }

    [Theory]
    [InlineData("Singleton", "")]
    [InlineData("Scoped", "")]
    [InlineData("Transient", "")]
    [InlineData("KeyedSingleton", "\"test\"")]
    [InlineData("KeyedScoped", "\"test\"")]
    [InlineData("KeyedTransient", "\"test\"")]
    public async Task OffendingMethods_CauseWarning(string lifetime, string key)
    {
        await RunAnalyzerAsync(
            $$"""
            using Test;
            using Microsoft.Extensions.DependencyInjection;

            var services = new ServiceCollection();
            {|BW0003:services.Add{{lifetime}}<IMyService, MyService>({{key}})|};
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
    public async Task CorrectMethods_NoDiagnostics(string lifetime, string key)
    {
        await RunAnalyzerAsync(
            $$"""
            using Test;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            var services = new ServiceCollection();
            services.TryAdd{{lifetime}}<IMyService, MyService>({{key}});
            """
        );
    }
}
