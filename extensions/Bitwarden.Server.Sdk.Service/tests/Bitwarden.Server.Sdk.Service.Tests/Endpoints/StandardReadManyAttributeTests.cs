using System.Reflection;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A read of a collection is a <c>GET</c> on the collection that succeeds with <c>200 OK</c> and is a read, so it takes the read scope.
/// </summary>
public sealed class StandardReadManyAttributeTests
{
    [Fact]
    public void Defaults_GetOnTheCollection()
    {
        var actual = new StandardReadManyAttribute();

        Assert.Equal(["GET"], actual.HttpMethods);
        Assert.Equal("", actual.Template);
        Assert.Equal(200, actual.SuccessStatusCode);
        Assert.True(((IStandardOperation)actual).IsRead);
    }

    [Fact]
    public void Template_Given_IsKept()
    {
        Assert.Equal("current", new StandardReadManyAttribute("current").Template);
    }

    [Fact]
    public void Usage_MethodsOnly()
    {
        var usage = typeof(StandardReadManyAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }
}
