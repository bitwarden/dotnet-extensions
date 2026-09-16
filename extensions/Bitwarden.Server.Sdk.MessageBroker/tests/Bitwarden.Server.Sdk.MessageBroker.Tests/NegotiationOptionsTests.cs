using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class NegotiationOptionsTests
{
    private static NegotiationOptions ValidOptions() => new()
    {
        ServiceName = "billing",
        ProcessDisplayName = "billing-pod-abc",
    };

    [Fact]
    public void ValidOptionsPass()
    {
        var result = new NegotiationOptionsValidator().Validate(null, ValidOptions());
        Assert.True(result.Succeeded, string.Join("; ", result.Failures ?? []));
    }

    [Fact]
    public void MissingServiceNameFails()
    {
        var options = ValidOptions();
        options.ServiceName = null;
        var result = new NegotiationOptionsValidator().Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(NegotiationOptions.ServiceName)));
    }

    [Fact]
    public void MissingDisplayNameFails()
    {
        var options = ValidOptions();
        options.ProcessDisplayName = null;
        var result = new NegotiationOptionsValidator().Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(NegotiationOptions.ProcessDisplayName)));
    }

    [Fact]
    public void EmptyControlTopicNameFails()
    {
        var options = ValidOptions();
        options.ControlTopicName = "";
        var result = new NegotiationOptionsValidator().Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(NegotiationOptions.ControlTopicName)));
    }

    [Fact]
    public void NonPositiveHeartbeatIntervalFails()
    {
        var options = ValidOptions();
        options.HeartbeatInterval = TimeSpan.Zero;
        var result = new NegotiationOptionsValidator().Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(NegotiationOptions.HeartbeatInterval)));
    }

    [Fact]
    public void TtlPaddingFactorAtOrBelowOneFails()
    {
        var options = ValidOptions();
        options.TtlPaddingFactor = 1.0;
        var result = new NegotiationOptionsValidator().Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(NegotiationOptions.TtlPaddingFactor)));
    }

    [Fact]
    public void CacheEntryTtlExceedsHeartbeatByPaddingFactor()
    {
        var options = new NegotiationOptions
        {
            ServiceName = "billing",
            ProcessDisplayName = "billing-pod-abc",
            HeartbeatInterval = TimeSpan.FromMinutes(5),
            TtlPaddingFactor = 1.2,
        };
        Assert.Equal(TimeSpan.FromMinutes(6), options.CacheEntryTtl);
    }

    [Fact]
    public void SubscriptionNamesDeriveFromServiceName()
    {
        var options = new NegotiationOptions { ServiceName = "billing", ProcessDisplayName = "x" };
        Assert.Equal("request-billing", options.RequestSubscriptionName);
        Assert.Equal("reply-billing", options.ReplySubscriptionName);
    }

    [Fact]
    public void SubscriptionNamesThrowWhenServiceNameNotSet()
    {
        var options = new NegotiationOptions { ProcessDisplayName = "x" };
        Assert.Throws<InvalidOperationException>(() => options.RequestSubscriptionName);
        Assert.Throws<InvalidOperationException>(() => options.ReplySubscriptionName);
    }

    [Fact]
    public void InstanceIdComposesDisplayNameAndProvidedGuid()
    {
        var id = Guid.Parse("6f0b9e1d-3a4c-49b3-8f2d-2f0a1c9a8b12");
        var options = new NegotiationOptions
        {
            ServiceName = "billing",
            ProcessDisplayName = "billing-pod-abc",
            Id = id,
        };
        Assert.Equal($"billing-pod-abc-{id}", options.InstanceId);
    }

    [Fact]
    public void InstanceIdGeneratesFreshGuidWhenIdNotSet()
    {
        var options = new NegotiationOptions
        {
            ServiceName = "billing",
            ProcessDisplayName = "billing-pod-abc",
        };
        var id = options.InstanceId;
        Assert.StartsWith("billing-pod-abc-", id);
        // The composed guid must parse back to a Guid.
        var guidPart = id["billing-pod-abc-".Length..];
        Assert.True(Guid.TryParse(guidPart, out _));
    }

    [Fact]
    public void InstanceIdIsStableAcrossMultipleReads()
    {
        var options = new NegotiationOptions
        {
            ServiceName = "billing",
            ProcessDisplayName = "billing-pod-abc",
        };
        var first = options.InstanceId;
        var second = options.InstanceId;
        Assert.Equal(first, second);
    }

    [Fact]
    public void InstanceIdThrowsWhenDisplayNameExplicitlyCleared()
    {
        var options = new NegotiationOptions { ServiceName = "billing", ProcessDisplayName = null };
        Assert.Throws<InvalidOperationException>(() => options.InstanceId);
    }

    [Fact]
    public void ProcessDisplayNameDefaultsToMachineName()
    {
        var options = new NegotiationOptions();
        Assert.Equal(Environment.MachineName, options.ProcessDisplayName);
    }
}
