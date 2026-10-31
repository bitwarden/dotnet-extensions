using System.Reflection;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// An update is a <c>PUT</c> by identifier that succeeds with <c>200 OK</c> and is not a read.
/// </summary>
public sealed class StandardUpdateAttributeTests
{
    [Fact]
    public void Defaults_PutByIdentifier()
    {
        var actual = new StandardUpdateAttribute();

        Assert.Equal(["PUT"], actual.HttpMethods);
        Assert.Equal("{id}", actual.Template);
        Assert.Equal(200, actual.SuccessStatusCode);
        Assert.False(((IStandardOperation)actual).IsRead);
    }

    [Fact]
    public void Template_Given_IsKept()
    {
        Assert.Equal("current", new StandardUpdateAttribute("current").Template);
    }

    [Fact]
    public void Usage_MethodsOnly()
    {
        var usage = typeof(StandardUpdateAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }
}
