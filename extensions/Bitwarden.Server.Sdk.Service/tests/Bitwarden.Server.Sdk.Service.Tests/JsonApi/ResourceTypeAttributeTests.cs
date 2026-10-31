using System.Reflection;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// The attribute carries the wire name it was given, and goes on a resource record only.
/// </summary>
public sealed class ResourceTypeAttributeTests
{
    [Fact]
    public void Type_IsTheNameGiven()
    {
        Assert.Equal("api-key", new ResourceTypeAttribute("api-key").Type);
    }

    [Fact]
    public void Usage_ClassesOnly_OncePerClass()
    {
        var usage = typeof(ResourceTypeAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }

    [Fact]
    public void Applied_ReadFromTheRecord()
    {
        Assert.Equal("gizmo", typeof(Renamed).GetCustomAttribute<ResourceTypeAttribute>()!.Type);
        Assert.Equal("gizmo", new Renamed().Type);
    }

    [ResourceType("gizmo")]
    public sealed record Renamed : Resource;
}
