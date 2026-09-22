namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests;

/// <summary>
/// A restricted service, its implementation, and the baseline vocabulary the rule tests share.
/// The type-level rule is gated; one member is tracked only and one is forbidden, so every
/// rule row is reachable from a single fixture.
/// </summary>
internal static class SampleProjectFixture
{
    public const string RepoRoot = "/repo/";
    public const string Project = "TestProject";
    public const string Type = "Test.IUserService";
    public const string ServicePath = "/repo/src/Core/Services/IUserService.cs";
    public const string ConsumerPath = "/repo/src/Api/Consumer.cs";

    public const string CanAccessPremium = "M:Test.IUserService.CanAccessPremium(Test.User)";
    public const string GetProperUserId = "M:Test.IUserService.GetProperUserId(Test.Principal)";
    public const string GetUserName = "M:Test.IUserService.GetUserName(Test.Principal)";

    public static readonly string[] DeclaredMembers = [CanAccessPremium, GetProperUserId, GetUserName];

    /// <summary>
    /// Documentation-comment id of <see cref="Consumer"/>'s constructor, which is where it injects
    /// the restricted type. This is the site a baselined injection row names.
    /// </summary>
    public const string ConstructorSite = "M:Test.Consumer.#ctor(Test.IUserService)";

    /// <summary>
    /// Documentation-comment id of <see cref="Consumer"/>'s only method, which is where it uses a
    /// restricted member. This is the site a baselined member row names.
    /// </summary>
    public const string RunSite = "M:Test.Consumer.Run(Test.User)";

    /// <summary>
    /// A complete, unexpired exception covering the restricted type. Every part is filled in, so a
    /// test using it exercises the valid path rather than tripping BW0010.
    /// </summary>
    public const string ValidException =
        """[RestrictedDependencyException(typeof(IUserService), Owner = "team-billing", Reason = "PM-12345", Expires = "2099-12-31")]""";

    public const string Service = """
        using System;
        using System.Threading.Tasks;
        using Bitwarden.Server.Sdk.RestrictedDependencies;

        namespace Test;

        public class User { }
        public class Principal { }

        [RestrictedDependency(AllowExistingUses = true, AllowNewUses = false, Tracking = "PM-1",
            SealMembers = true, AllowedPaths = new[] { "src/Core/Services/**" })]
        public interface IUserService
        {
            Task<bool> CanAccessPremium(User user);

            [RestrictedDependency(AllowExistingUses = true, AllowNewUses = true, Tracking = "L3-GS-1")]
            Guid? GetProperUserId(Principal principal);

            [RestrictedDependency(AllowExistingUses = false, AllowNewUses = false, Replacement = "IUserNameQuery")]
            string GetUserName(Principal principal);
        }

        public class UserService : IUserService
        {
            public Task<bool> CanAccessPremium(User user) => Task.FromResult(true);
            public Guid? GetProperUserId(Principal principal) => null;
            public string GetUserName(Principal principal) => string.Empty;
            public static bool IsLegacyUser(User user) => false;
        }
        """;

    /// <summary>
    /// A consumer that injects the restricted type and calls one gated member once: the smallest
    /// consumer whose baseline needs both an injection row and a member row.
    /// </summary>
    public const string Consumer = """
        using System.Threading.Tasks;
        using Test;

        namespace Test;

        public class Consumer
        {
            private readonly IUserService _userService;

            public Consumer(IUserService userService)
            {
                _userService = userService;
            }

            public Task<bool> Run(User user) => _userService.CanAccessPremium(user);
        }
        """;

    /// <summary>
    /// <see cref="Consumer"/> up to and including its constructor, for a test that appends the
    /// methods its case needs and closes the class itself.
    /// </summary>
    public const string ConsumerPreamble = """
        using System;
        using System.Threading.Tasks;
        using Test;

        namespace Test;

        public class Consumer
        {
            private readonly IUserService _userService;

            public Consumer(IUserService userService)
            {
                _userService = userService;
            }

        """;

    /// <summary>
    /// <see cref="Service"/> with the interface name marked as location 0. Every diagnostic that
    /// points at the restricted type's declaration anchors to the source this way, so adding a
    /// line above it does not move a coordinate any test has to know.
    /// </summary>
    public static readonly string ServiceWithMarkedInterface =
        Service.Replace("public interface IUserService", "public interface {|#0:IUserService|}");

    public static string Baseline(params BudgetEntry[] usages) =>
        new BudgetModel(Type, DeclaredMembers, usages).Serialize();

    public static BudgetEntry Site(DependencyUsageType kind, string site, int count = 1) =>
        new(kind, null, Project, site, count);

    public static BudgetEntry MemberSite(string member, string site, int count = 1) =>
        new(DependencyUsageType.Member, member, Project, site, count);
}
