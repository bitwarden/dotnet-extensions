namespace Bitwarden.Server.Sdk.Context.Tests;

public sealed class EventContextTests
{
    // Anything handed the context can tell an event's actor from a person acting, and reads everything the event said.
    [Fact]
    public void EveryMember_IsWhatTheEventSaid_AndItIsAnEvents()
    {
        var said = new StatedContext
        {
            OrganizationId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ProviderId = Guid.NewGuid(),
            ClientIpAddress = "203.0.113.9",
            DeviceType = "10",
            DeviceIdentifier = "device-one",
            Credential = CallerCredential.User,
            RecordedUserId = Guid.NewGuid(),
            RecordedProviderId = Guid.NewGuid(),
            UserOrganizationIds = [Guid.NewGuid()],
            FlagSubjectKind = FlagSubjectKind.Organization,
            FlagSubjectKey = Guid.NewGuid(),
            ClientVersion = "2026.10.1",
            ClientVersionIsPrerelease = true,
        };

        IContext context = new EventContext(said);

        // Every member but IsEvent reads what the event said, so a member added to IContext and not passed on fails.
        foreach (var member in typeof(IContext).GetProperties().Where(member => member.Name != nameof(IContext.IsEvent)))
        {
            Assert.Equal(member.GetValue(said), member.GetValue(context));
        }

        Assert.True(context.IsEvent);
        Assert.False(((IContext)said).IsEvent);
    }
}
