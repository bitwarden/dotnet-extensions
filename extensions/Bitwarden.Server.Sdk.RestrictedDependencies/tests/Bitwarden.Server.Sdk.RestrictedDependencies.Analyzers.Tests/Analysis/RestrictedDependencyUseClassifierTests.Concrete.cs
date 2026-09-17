using System.Diagnostics.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests.Analysis;

public partial class RestrictedDependencyUseClassifierTests
{
    private const string StaticAccessOnImplementation = """
        public class Consumer
        {
            public bool Run(User user) => {|BW0008:UserService.IsLegacyUser(user)|};
        }
        """;

    private const string NewImplementation = """
        public class Consumer
        {
            public object Run() => {|BW0008:new UserService()|};
        }
        """;

    private const string TypeOfOutsideARegistration = """
        public class Consumer
        {
            public Type Run() => {|BW0008:typeof(UserService)|};
        }
        """;

    private const string ImplementationInAConstructorSignature = """
        public class Consumer
        {
            public Consumer(UserService {|BW0008:userService|})
            {
            }
        }
        """;

    private const string InheritingFromImplementation = """
        public class {|BW0008:Consumer|} : UserService
        {
        }
        """;

    [Theory]
    [InlineData(StaticAccessOnImplementation)]
    [InlineData(NewImplementation)]
    [InlineData(TypeOfOutsideARegistration)]
    [InlineData(ImplementationInAConstructorSignature)]
    [InlineData(InheritingFromImplementation)]
    public async Task ReferenceToTheImplementation_NotBaselined_ReportsConcrete([StringSyntax("C#-test")] string consumer)
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer($$"""
                using System;
                using Test;

                namespace Test;

                {{consumer}}
                """)
            .RunAsync();
    }

    [Fact]
    public async Task TypeOfInsideAnAttribute_ReportsNothing()
    {
        // Metadata, not a call: an attribute naming the implementation is how ASP.NET filters and
        // converters are wired, and the reference does nothing at the site.
        await AnalyzerHarness.WithBaseline()
            .WithConsumer("""
                using System;
                using Test;

                namespace Test;

                public class KeepAttribute(Type type) : Attribute
                {
                    public Type Type { get; } = type;
                }

                [Keep(typeof(UserService))]
                public class Consumer
                {
                }
                """)
            .RunAsync();
    }

    [Theory]
    [InlineData("services.AddScoped<IUserService, UserService>();")]
    [InlineData("services.TryAddScoped<IUserService, UserService>();")]
    [InlineData("services.AddScoped(typeof(IUserService), typeof(UserService));")]
    public async Task DependencyInjectionRegistration_IsExempt([StringSyntax("C#-test")] string registration)
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer($$"""
                using Microsoft.Extensions.DependencyInjection;
                using Microsoft.Extensions.DependencyInjection.Extensions;
                using Test;

                namespace Test;

                public static class Registrations
                {
                    public static void Register(IServiceCollection services)
                    {
                        {{registration}}
                    }
                }
                """)
            .RunAsync();
    }

    private const string SubInterfacePath = "/repo/src/Core/Services/IUserServiceEx.cs";

    private const string SubInterface = """
        using System;
        using System.Threading.Tasks;
        using Test;

        namespace Test;

        public interface IUserServiceEx : IUserService
        {
            Task Extra(User user);
        }

        public class UserServiceEx : IUserServiceEx
        {
            public Task<bool> CanAccessPremium(User user) => Task.FromResult(true);
            public Guid? GetProperUserId(Principal principal) => null;
            public string GetUserName(Principal principal) => string.Empty;
            public Task Extra(User user) => Task.CompletedTask;
        }
        """;

    /// <summary>
    /// An interface that extends a restricted interface carries every restricted member, so using
    /// it is using the restricted type: it counts as an implementation and needs a baseline row or
    /// an exception like any other. Otherwise ordinary typed code routes straight around the
    /// ratchet.
    /// </summary>
    [Fact]
    public async Task SubInterfaceOfARestrictedInterface_NotBaselined_ReportsConcrete()
    {
        await AnalyzerHarness.WithBaseline()
            .WithSource(SubInterfacePath, SubInterface)
            .WithConsumer("""
                using Test;

                namespace Test;

                public class Consumer
                {
                    public Consumer(IUserServiceEx {|BW0008:userService|})
                    {
                    }
                }
                """)
            .RunAsync();
    }

    [Fact]
    public async Task SubInterfaceRegistration_IsExempt()
    {
        await AnalyzerHarness.WithBaseline()
            .WithSource(SubInterfacePath, SubInterface)
            .WithConsumer("""
                using Microsoft.Extensions.DependencyInjection;
                using Test;

                namespace Test;

                public static class Registrations
                {
                    public static void Register(IServiceCollection services)
                    {
                        services.AddScoped<IUserServiceEx, UserServiceEx>();
                    }
                }
                """)
            .RunAsync();
    }
}
