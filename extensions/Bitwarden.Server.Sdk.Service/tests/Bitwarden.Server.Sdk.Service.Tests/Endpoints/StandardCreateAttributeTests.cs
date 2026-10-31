using System.Reflection;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A create is a <c>POST</c> to the collection that succeeds with <c>201 Created</c> and is not a read.
/// </summary>
public sealed class StandardCreateAttributeTests
{
    [Fact]
    public void Defaults_PostToTheCollection()
    {
        var actual = new StandardCreateAttribute();

        Assert.Equal(["POST"], actual.HttpMethods);
        Assert.Equal(string.Empty, actual.Template);
        Assert.Equal(201, actual.SuccessStatusCode);
        Assert.False(((IStandardOperation)actual).IsRead);
    }

    [Fact]
    public void Template_Given_IsKept()
    {
        Assert.Equal("{id}/copies", new StandardCreateAttribute("{id}/copies").Template);
    }

    [Fact]
    public void Usage_MethodsOnly()
    {
        var usage = typeof(StandardCreateAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }
}
