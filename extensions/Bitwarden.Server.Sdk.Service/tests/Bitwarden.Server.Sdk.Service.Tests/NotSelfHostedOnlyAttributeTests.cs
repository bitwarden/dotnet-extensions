using System.Reflection;

namespace Bitwarden.Server.Sdk.Service.Tests;

/// <summary>
/// Marks an operation only a cloud install has, on a handler or on its whole class; how a self-hosted install
/// refuses it is pinned in <see cref="SelfHostingTests"/>.
/// </summary>
public sealed class NotSelfHostedOnlyAttributeTests
{
    [Fact]
    public void Usage_OnAHandlerOrItsClass_Once()
    {
        var usage = typeof(NotSelfHostedOnlyAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Class | AttributeTargets.Method, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }

    [Fact]
    public void ReadFromAHandler()
    {
        var handler = typeof(NotSelfHostedOnlyAttributeTests).GetMethod(nameof(Marked), BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.NotNull(handler.GetCustomAttribute<NotSelfHostedOnlyAttribute>());
    }

    [NotSelfHostedOnly]
    private static void Marked()
    {
    }
}
