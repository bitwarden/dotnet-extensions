using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

/// <summary>
/// Unit-level coverage of <see cref="AzureServiceBusRuleReconciler.ComputeDiff"/>. The full
/// <see cref="AzureServiceBusRuleReconciler.ReconcileAsync"/> path exercises the
/// Azure Service Bus management REST API, which the emulator does not support, so its
/// SDK-facing wrapper is intentionally not tested at CI level — it is a thin translation of the
/// diff into <c>DeleteRuleAsync</c> / <c>CreateRuleAsync</c> calls.
/// </summary>
public class AzureServiceBusRuleReconcilerTests
{
    [Fact]
    public void AddsRulesForDesiredTopicsAndRemovesEverythingElse()
    {
        var (toAdd, toRemove) = AzureServiceBusRuleReconciler.ComputeDiff(
            currentRuleNames: ["$Default"],
            dataTopics: ["alpha", "beta"]);

        Assert.Equal(["$Default"], toRemove.Order());
        Assert.Equal(["data-topic-alpha", "data-topic-beta"], toAdd.Order());
    }

    [Fact]
    public void IdempotentWhenAlreadyReconciled()
    {
        var (toAdd, toRemove) = AzureServiceBusRuleReconciler.ComputeDiff(
            currentRuleNames: ["data-topic-alpha", "data-topic-beta"],
            dataTopics: ["alpha", "beta"]);

        Assert.Empty(toAdd);
        Assert.Empty(toRemove);
    }

    [Fact]
    public void RemovesRulesForTopicsNoLongerServed()
    {
        var (toAdd, toRemove) = AzureServiceBusRuleReconciler.ComputeDiff(
            currentRuleNames: ["data-topic-alpha", "data-topic-beta"],
            dataTopics: ["alpha"]);

        Assert.Empty(toAdd);
        Assert.Equal(["data-topic-beta"], toRemove);
    }
}
