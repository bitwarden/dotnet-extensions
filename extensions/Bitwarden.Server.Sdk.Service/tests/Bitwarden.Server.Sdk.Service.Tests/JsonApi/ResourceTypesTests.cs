using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// A resource's <c>type</c>: the record's name in lowercase kebab case, singular, unless the
/// record declares otherwise.
/// </summary>
public sealed class ResourceTypesTests
{
    [Theory]
    [InlineData("User", "user")]
    [InlineData("ApiKey", "api-key")]
    [InlineData("SsoConfig", "sso-config")]
    [InlineData("HTTPServer", "http-server")]
    [InlineData("SSO", "sso")]
    [InlineData("V2Key", "v2-key")]
    [InlineData("sendEmail", "send-email")]
    [InlineData("send-email", "send-email")]
    [InlineData("Users", "users")]
    [InlineData("", "")]
    public void KebabCase_Name_Converted(string name, string expected)
    {
        Assert.Equal(expected, ResourceTypes.KebabCase(name));
    }

    [Fact]
    public void For_NoAttribute_KebabCasesTheName()
    {
        Assert.Equal("api-key", ResourceTypes.For(typeof(ApiKey)));
    }

    [Fact]
    public void For_Attribute_TakesTheDeclaredName()
    {
        Assert.Equal("gizmo", ResourceTypes.For(typeof(Renamed)));
    }

    // A blank declaration names nothing, so the convention still applies.
    [Fact]
    public void For_BlankAttribute_FallsBackToTheConvention()
    {
        Assert.Equal("blank-named", ResourceTypes.For(typeof(BlankNamed)));
    }

    [Fact]
    public void For_CalledTwice_SameString()
    {
        Assert.Same(ResourceTypes.For(typeof(ApiKey)), ResourceTypes.For(typeof(ApiKey)));
    }

    public sealed record ApiKey : Resource;

    [ResourceType("gizmo")]
    public sealed record Renamed : Resource;

    [ResourceType("  ")]
    public sealed record BlankNamed : Resource;
}
