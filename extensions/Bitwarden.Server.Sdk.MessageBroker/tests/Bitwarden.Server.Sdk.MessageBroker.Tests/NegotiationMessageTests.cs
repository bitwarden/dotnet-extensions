using System.Text.Json;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class NegotiationMessageTests
{
    [Fact]
    public void CapabilityRoundTrips()
    {
        var original = new Capability
        {
            DataTopic = "user-events",
            FamilyWireName = "user.created",
            InstanceId = "billing-pod-abc-6f0b9e1d-3a4c-49b3-8f2d-2f0a1c9a8b12",
            WireNames = new HashSet<string> { "v3", "v2", "v1" },
        };
        var json = JsonSerializer.Serialize(original);
        var round = JsonSerializer.Deserialize<Capability>(json);
        Assert.NotNull(round);
        Assert.Equal(original.DataTopic, round.DataTopic);
        Assert.Equal(original.FamilyWireName, round.FamilyWireName);
        Assert.Equal(original.InstanceId, round.InstanceId);
        Assert.True(original.WireNames.SetEquals(round.WireNames));
    }

    [Fact]
    public void CapabilityUsesWirePropertyNames()
    {
        var value = new Capability
        {
            DataTopic = "user-events",
            FamilyWireName = "user.created",
            InstanceId = "billing-pod-abc-" + Guid.NewGuid(),
            WireNames = new HashSet<string> { "v1" },
        };
        var json = JsonSerializer.Serialize(value);
        Assert.Contains("\"dataTopic\":", json);
        Assert.Contains("\"familyWireName\":", json);
        Assert.Contains("\"instanceId\":", json);
        Assert.Contains("\"wireNames\":", json);
    }

    [Fact]
    public void PublisherJoinRoundTrips()
    {
        var original = new PublisherJoin
        {
            DataTopic = "user-events",
            FamilyWireName = "user.created",
            InstanceId = "identity-pod-xyz-9c1b3a7f-1e5d-4a0c-9f81-77c2d1f4a3b0",
            WireNames = new HashSet<string> { "v4", "v3" },
        };
        var json = JsonSerializer.Serialize(original);
        var round = JsonSerializer.Deserialize<PublisherJoin>(json);
        Assert.NotNull(round);
        Assert.Equal(original.DataTopic, round.DataTopic);
        Assert.Equal(original.FamilyWireName, round.FamilyWireName);
        Assert.Equal(original.InstanceId, round.InstanceId);
        Assert.True(original.WireNames.SetEquals(round.WireNames));
    }

    [Fact]
    public void PublisherLeaveRoundTrips()
    {
        var original = new PublisherLeave
        {
            DataTopic = "user-events",
            FamilyWireName = "user.deleted",
            InstanceId = "identity-pod-xyz-9c1b3a7f-1e5d-4a0c-9f81-77c2d1f4a3b0",
        };
        var json = JsonSerializer.Serialize(original);
        var round = JsonSerializer.Deserialize<PublisherLeave>(json);
        Assert.NotNull(round);
        Assert.Equal(original.DataTopic, round.DataTopic);
        Assert.Equal(original.FamilyWireName, round.FamilyWireName);
        Assert.Equal(original.InstanceId, round.InstanceId);
    }

    [Fact]
    public void SubscriberLeaveRoundTrips()
    {
        var original = new SubscriberLeave
        {
            DataTopic = "user-events",
            FamilyWireName = "user.created",
            InstanceId = "billing-pod-abc-6f0b9e1d-3a4c-49b3-8f2d-2f0a1c9a8b12",
        };
        var json = JsonSerializer.Serialize(original);
        var round = JsonSerializer.Deserialize<SubscriberLeave>(json);
        Assert.NotNull(round);
        Assert.Equal(original.DataTopic, round.DataTopic);
        Assert.Equal(original.FamilyWireName, round.FamilyWireName);
        Assert.Equal(original.InstanceId, round.InstanceId);
    }

    [Fact]
    public void NegotiationAckRoundTripsGo()
    {
        var original = new NegotiationAck { Go = true };
        var json = JsonSerializer.Serialize(original);
        var round = JsonSerializer.Deserialize<NegotiationAck>(json);
        Assert.NotNull(round);
        Assert.True(round.Go);
        Assert.Empty(round.Offenders);
    }

    [Fact]
    public void NegotiationAckRoundTripsNoGoWithMultipleOffenders()
    {
        var original = new NegotiationAck
        {
            Go = false,
            Offenders =
            [
                new NegotiationIncompatibility
                {
                    InstanceId = "billing-pod-abc-6f0b9e1d-3a4c-49b3-8f2d-2f0a1c9a8b12",
                    FamilyWireName = "user.created",
                    WireNames = new HashSet<string> { "v5" },
                },
                new NegotiationIncompatibility
                {
                    InstanceId = "identity-pod-xyz-9c1b3a7f-1e5d-4a0c-9f81-77c2d1f4a3b0",
                    FamilyWireName = "user.created",
                    WireNames = new HashSet<string> { "v5", "v6" },
                },
            ],
        };
        var json = JsonSerializer.Serialize(original);
        var round = JsonSerializer.Deserialize<NegotiationAck>(json);
        Assert.NotNull(round);
        Assert.False(round.Go);
        Assert.Equal(2, round.Offenders.Count);
        Assert.Equal(original.Offenders[0].InstanceId, round.Offenders[0].InstanceId);
        Assert.Equal(original.Offenders[0].FamilyWireName, round.Offenders[0].FamilyWireName);
        Assert.True(original.Offenders[0].WireNames.SetEquals(round.Offenders[0].WireNames));
        Assert.Equal(original.Offenders[1].InstanceId, round.Offenders[1].InstanceId);
        Assert.Equal(original.Offenders[1].FamilyWireName, round.Offenders[1].FamilyWireName);
        Assert.True(original.Offenders[1].WireNames.SetEquals(round.Offenders[1].WireNames));
    }
}
