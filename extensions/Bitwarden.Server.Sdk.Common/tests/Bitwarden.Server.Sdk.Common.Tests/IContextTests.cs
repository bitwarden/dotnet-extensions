namespace Bitwarden.Server.Sdk.Common.Tests;

// Members added after the three owners are defaulted, so an implementation predating them keeps
// compiling and answers "unknown" for each.
public sealed class IContextTests
{
    [Fact]
    public void Defaults_ImplementingOnlyTheOwners_KnowNothingElse()
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

    private sealed class OwnersOnly : IContext
    {
        public Guid? OrganizationId => null;

        public Guid? UserId => null;

        public Guid? ProviderId => null;
    }
}
