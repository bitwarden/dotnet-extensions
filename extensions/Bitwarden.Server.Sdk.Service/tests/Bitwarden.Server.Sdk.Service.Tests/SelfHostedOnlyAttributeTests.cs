using System.Reflection;

namespace Bitwarden.Server.Sdk.Service.Tests;

/// <summary>
/// Marks an operation only a self-hosted install has, on a handler or on its whole class; how a cloud install
/// refuses it is pinned in <see cref="SelfHostingTests"/>.
/// </summary>
public sealed class SelfHostedOnlyAttributeTests
{
    [Fact]
    public void Usage_OnAHandlerOrItsClass_Once()
    {
        var usage = typeof(SelfHostedOnlyAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Class | AttributeTargets.Method, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }

    [Fact]
    public void ReadFromAHandler()
    {
        var handler = typeof(SelfHostedOnlyAttributeTests).GetMethod(nameof(Marked), BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.NotNull(handler.GetCustomAttribute<SelfHostedOnlyAttribute>());
    }

    [SelfHostedOnly]
    private static void Marked()
    {
    }
}
