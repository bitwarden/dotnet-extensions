using System.Reflection;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A delete is a <c>DELETE</c> by identifier that succeeds with <c>204 No Content</c> and is not a read.
/// </summary>
public sealed class StandardDeleteAttributeTests
{
    [Fact]
    public void Defaults_DeleteByIdentifier()
    {
        var actual = new StandardDeleteAttribute();

        Assert.Equal(["DELETE"], actual.HttpMethods);
        Assert.Equal("{id}", actual.Template);
        Assert.Equal(204, actual.SuccessStatusCode);
        Assert.False(((IStandardOperation)actual).IsRead);
    }

    [Fact]
    public void Template_Given_IsKept()
    {
        Assert.Equal("current", new StandardDeleteAttribute("current").Template);
    }

    [Fact]
    public void Usage_MethodsOnly()
    {
        var usage = typeof(StandardDeleteAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }
}
