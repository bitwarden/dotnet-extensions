using System.Reflection;

namespace Bitwarden.Server.Sdk.Common.Tests;

public sealed class ServiceExtensionAttributeTests
{
    [Fact]
    public void Extension_IsTheTypeGiven()
    {
        Assert.Equal(typeof(string), new ServiceExtensionAttribute(typeof(string)).Extension);
    }

    [Fact]
    public void Usage_IsAssembliesOnly_AndMayRepeat()
    {
        var usage = typeof(ServiceExtensionAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Assembly, usage.ValidOn);
        Assert.True(usage.AllowMultiple);
    }
}
