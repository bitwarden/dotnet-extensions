using System.Reflection;

namespace Bitwarden.Server.Sdk.Service.Tests;

/// <summary>
/// An endpoints class's section: a name only where the default split is wrong, a description written for it, and
/// one tag per class.
/// </summary>
public sealed class ApiTagAttributeTests
{
    [Fact]
    public void WithoutAName_TheClassNameIsSplit()
    {
        var tag = new ApiTagAttribute();

        Assert.Null(tag.Name);
        Assert.Null(tag.Description);
    }

    [Fact]
    public void ANameAndADescription_AreKept()
    {
        var tag = new ApiTagAttribute("SSO Configurations") { Description = "How an organization signs its members in." };

        Assert.Equal("SSO Configurations", tag.Name);
        Assert.Equal("How an organization signs its members in.", tag.Description);
    }

    // One section per endpoints class: it goes on the class, once.
    [Fact]
    public void Usage_OncePerClass()
    {
        var usage = typeof(ApiTagAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }

    [Fact]
    public void ReadFromTheClass()
    {
        var tag = typeof(TaggedEndpoints).GetCustomAttribute<ApiTagAttribute>()!;

        Assert.Equal("Tagged", tag.Name);
        Assert.Equal("Things that are tagged.", tag.Description);
    }

    [ApiTag("Tagged", Description = "Things that are tagged.")]
    private static class TaggedEndpoints;
}
