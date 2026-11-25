namespace Bitwarden.Server.Sdk.Context.Tests;

public sealed class EventContextTests
{
    private static readonly string[] Recorded =
    [
        nameof(IContext.UserId), nameof(IContext.ProviderId), nameof(IContext.Credential),
        nameof(IContext.RecordedUserId), nameof(IContext.RecordedProviderId), nameof(IContext.IsEvent),
    ];

    [Fact]
    public void TheEventsActor_IsRecorded_NotActing()
    {
        var said = Said(credential: CallerCredential.User);

        IContext context = new EventContext(said);

        Assert.Null(context.UserId);
        Assert.Null(context.ProviderId);
        Assert.Equal(CallerCredential.System, context.Credential);
        Assert.Equal(said.UserId, context.RecordedUserId);
        Assert.Equal(said.ProviderId, context.RecordedProviderId);
        Assert.True(context.IsEvent);
        Assert.False(((IContext)said).IsEvent);
    }

    [Fact]
    public void AnEventPublishedOnASystemCall_KeepsWhomItRecorded()
    {
        var said = Said(credential: CallerCredential.System) with { UserId = null, ProviderId = null };

        IContext context = new EventContext(said);

        Assert.Equal(said.RecordedUserId, context.RecordedUserId);
        Assert.Equal(said.RecordedProviderId, context.RecordedProviderId);
    }

    // A member added to IContext and not passed on fails.
    [Fact]
    public void EveryOtherMember_IsWhatTheEventSaid()
    {
        var said = Said(credential: CallerCredential.User);

        IContext context = new EventContext(said);

        foreach (var member in typeof(IContext).GetProperties().Where(member => !Recorded.Contains(member.Name)))
        {
            Assert.Equal(member.GetValue(said), member.GetValue(context));
        }
    }

    private static StatedContext Said(CallerCredential credential) => new()
    {
        OrganizationId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        ProviderId = Guid.NewGuid(),
        ClientIpAddress = "203.0.113.9",
        DeviceType = "10",
        DeviceIdentifier = "device-one",
        Credential = credential,
        RecordedUserId = Guid.NewGuid(),
        RecordedProviderId = Guid.NewGuid(),
        UserOrganizationIds = [Guid.NewGuid()],
        FlagSubjectKind = FlagSubjectKind.Organization,
        FlagSubjectKey = Guid.NewGuid(),
        ClientVersion = "2026.10.1",
        ClientVersionIsPrerelease = true,
    };
}
