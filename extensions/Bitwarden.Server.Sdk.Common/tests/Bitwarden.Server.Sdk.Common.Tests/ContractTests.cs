using Bitwarden.Server.Sdk.Common.Errors;

namespace Bitwarden.Server.Sdk.Common.Tests;

// The small agreements a service and its callers share: defaults an older implementation relies on,
// the headers refused from outside, the cache's key format, and the standard error sentences.
public sealed class ContractTests
{
    [Fact]
    public void Context_ImplementingOnlyTheOwners_KnowsNothingElse()
    {
        IContext context = new OwnersOnly();

        Assert.Null(context.ClientIpAddress);
        Assert.Null(context.DeviceType);
        Assert.Null(context.DeviceIdentifier);
        Assert.Null(context.Credential);
        Assert.Null(context.RecordedUserId);
        Assert.Null(context.RecordedProviderId);
        Assert.Null(context.UserOrganizationIds);
    }

    [Fact]
    public void ServiceIdentity_ByDefault_IsADomainServiceAcceptingThePlatform()
    {
        IServiceIdentity identity = new DomainIdentity();

        Assert.False(identity.IsProductService);
        Assert.Equal(TokenAudience.Platform, identity.Audience);
    }

    [Fact]
    public void ServiceIdentity_AProductService_AcceptsItsOwnName()
    {
        IServiceIdentity identity = new ProductIdentity();

        Assert.Equal("widget-service", identity.Audience);
    }

    [Fact]
    public void Headers_All_AreTheFourCallerContextHeaders()
    {
        Assert.Equal(
            ["Bitwarden-Organization", "Bitwarden-User", "Bitwarden-Provider", "Bitwarden-Context"],
            BitwardenHeaders.All);
    }

    [Fact]
    public void Readiness_YesAndNo_SayWhetherAndWhy()
    {
        Assert.True(ReadinessResult.Yes.Ready);
        Assert.Null(ReadinessResult.Yes.Reason);

        var no = ReadinessResult.No("The cache is unreachable.");
        Assert.False(no.Ready);
        Assert.Equal("The cache is unreachable.", no.Reason);
    }

    [Fact]
    public void ServiceExtensionAttribute_KeepsTheExtensionType()
    {
        Assert.Equal(typeof(NoExtension), new ServiceExtensionAttribute(typeof(NoExtension)).Extension);
    }

    [Fact]
    public void OwnedCacheEntry_KeysSitUnderTheOwningServicesPrefix()
    {
        var id = Guid.Parse("a0000000-0000-4000-8000-000000000001");

        var template = OwnedCacheEntry.Template("widget-service", "widget");

        Assert.Equal("widget-service:", OwnedCacheEntry.Prefix("widget-service"));
        Assert.Equal("widget-service:widget:{key}", template);
        Assert.Equal("widget-service:widget:a0000000-0000-4000-8000-000000000001", OwnedCacheEntry.Key(template, id));
        Assert.StartsWith(OwnedCacheEntry.Prefix("widget-service"), OwnedCacheEntry.Key(template, id));
    }

    [Fact]
    public void ErrorDetails_NameWhatWasWrong()
    {
        Assert.Equal("The resource does not support PATCH requests.", ErrorDetails.MethodNotAllowed("PATCH"));
        Assert.Equal("The 'name' field is required.", ErrorDetails.FieldRequired("name"));
        Assert.Equal("The 'kind' field must be one of: a, b.", ErrorDetails.NotAPermittedValue("kind", ["a", "b"]));
        Assert.Equal("The 'count' field must be a number.", ErrorDetails.InvalidType("count", "a number"));
        Assert.Equal("The 'email' value is already in use.", ErrorDetails.AlreadyExists("email"));
        Assert.Equal(
            "A resource of type 'widget' was expected, but 'gadget' was declared.",
            ErrorDetails.InvalidResourceType("widget", "gadget"));
    }

    private sealed class OwnersOnly : IContext
    {
        public Guid? OrganizationId => null;

        public Guid? UserId => null;

        public Guid? ProviderId => null;
    }

    private sealed class DomainIdentity : IServiceIdentity
    {
        public string Name => "widget-service";

        public string Title => "Widget Service";
    }

    private sealed class ProductIdentity : IServiceIdentity
    {
        public string Name => "widget-service";

        public string Title => "Widget Service";

        public bool IsProductService => true;
    }

    private sealed class NoExtension : IServiceExtension
    {
        public void Configure(
            Microsoft.Extensions.DependencyInjection.IServiceCollection services,
            Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
        }
    }
}
