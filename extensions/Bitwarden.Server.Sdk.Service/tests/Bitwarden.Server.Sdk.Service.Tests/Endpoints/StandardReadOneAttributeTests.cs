using System.Reflection;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A read of one resource is a <c>GET</c> by identifier that succeeds with <c>200 OK</c> and is a read, so it takes the read scope.
/// </summary>
public sealed class StandardReadOneAttributeTests
{
    [Fact]
    public void Defaults_GetByIdentifier()
    {
        var actual = new StandardReadOneAttribute();

        Assert.Equal(["GET"], actual.HttpMethods);
        Assert.Equal("{id}", actual.Template);
        Assert.Equal(200, actual.SuccessStatusCode);
        Assert.True(((IStandardOperation)actual).IsRead);
    }

    [Fact]
    public void Template_Given_IsKept()
    {
        Assert.Equal("current", new StandardReadOneAttribute("current").Template);
    }

    [Fact]
    public void Usage_MethodsOnly()
    {
        var usage = typeof(StandardReadOneAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }
}
