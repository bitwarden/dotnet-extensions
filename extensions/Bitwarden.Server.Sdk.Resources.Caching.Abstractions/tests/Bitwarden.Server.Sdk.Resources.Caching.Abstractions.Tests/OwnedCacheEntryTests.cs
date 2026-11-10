namespace Bitwarden.Server.Sdk.Resources.Caching.Abstractions.Tests;

public sealed class OwnedCacheEntryTests
{
    private static readonly Guid Id = Guid.Parse("a0000000-0000-4000-8000-000000000001");

    [Fact]
    public void Prefix_IsTheServiceName()
    {
        Assert.Equal("widget-service:", OwnedCacheEntry.Prefix("widget-service"));
    }

    [Fact]
    public void Template_IsServiceTypeAndPlaceholder()
    {
        Assert.Equal("widget-service:widget:{key}", OwnedCacheEntry.Template("widget-service", "widget"));
    }

    [Theory]
    [InlineData("widget-service", "widget:by-name")]
    [InlineData("widget-service", "widget{key}")]
    [InlineData("widget-service", "widget}")]
    [InlineData("widget-service", "")]
    [InlineData("widget-service", " ")]
    [InlineData("widget:service", "widget")]
    [InlineData("widget{service", "widget")]
    [InlineData("widget}service", "widget")]
    [InlineData(" ", "widget")]
    public void Template_ANameThatCouldBeConfused_Throws(string service, string resourceType)
    {
        Assert.Throws<ArgumentException>(() => OwnedCacheEntry.Template(service, resourceType));
    }

    // Each segment a service's other keys sit under in its prefix.
    [Theory]
    [InlineData("answer")]
    [InlineData("index")]
    [InlineData("gen")]
    [InlineData("idempotency")]
    public void Template_AReservedType_Throws(string resourceType)
    {
        var exception = Assert.Throws<ArgumentException>(() => OwnedCacheEntry.Template("widget-service", resourceType));

        Assert.Equal("resourceType", exception.ParamName);
    }

    [Fact]
    public void Template_ATypeOnlyLikeTheAnswersType_IsAllowed()
    {
        Assert.Equal("widget-service:answers:{key}", OwnedCacheEntry.Template("widget-service", "answers"));
    }

    [Theory]
    [InlineData("widget:service")]
    [InlineData("widget{service")]
    [InlineData("widget}service")]
    [InlineData("")]
    public void Prefix_ANameThatCouldBeConfused_Throws(string service)
    {
        Assert.Throws<ArgumentException>(() => OwnedCacheEntry.Prefix(service));
    }

    [Fact]
    public void Key_FillsTheTemplate_UnderTheOwnersPrefix()
    {
        var key = OwnedCacheEntry.Key(OwnedCacheEntry.Template("widget-service", "widget"), Id);

        Assert.Equal("widget-service:widget:a0000000-0000-4000-8000-000000000001", key);
        Assert.StartsWith(OwnedCacheEntry.Prefix("widget-service"), key);
    }

    [Fact]
    public void Fields_AreTheContract()
    {
        Assert.Equal("v", OwnedCacheEntry.Version);
        Assert.Equal("o", OwnedCacheEntry.Owner);
        Assert.Equal("d", OwnedCacheEntry.Data);
        Assert.Equal("none", OwnedCacheEntry.None);
        Assert.Equal("gone", OwnedCacheEntry.Gone);
        Assert.Equal("x-bitwarden-cache", OwnedCacheEntry.Extension);
    }
}
