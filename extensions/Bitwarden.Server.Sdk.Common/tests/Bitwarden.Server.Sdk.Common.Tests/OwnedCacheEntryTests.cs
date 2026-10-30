namespace Bitwarden.Server.Sdk.Common.Tests;

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
