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

    // Redis keys are case-sensitive, so only the exact reserved segment would mix with the service's other keys.
    [Fact]
    public void Template_AReservedTypeInAnotherCase_IsAllowed()
    {
        Assert.Equal("widget-service:Answer:{key}", OwnedCacheEntry.Template("widget-service", "Answer"));
    }

    [Fact]
    public void Key_NullTemplate_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => OwnedCacheEntry.Key(null!, Id, null, null, null));
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
    public void KeyedTemplate_UnderTheKeysName()
    {
        Assert.Equal("widget-service:widget:by-maker:{key}", OwnedCacheEntry.KeyedTemplate("widget-service", "widget", "maker", null));
        Assert.Equal(
            "widget-service:widget:by-maker:{organization}:{key}",
            OwnedCacheEntry.KeyedTemplate("widget-service", "widget", "maker", "organization"));
        Assert.Throws<ArgumentException>(() => OwnedCacheEntry.KeyedTemplate("widget-service", "widget", "maker", "team"));
        Assert.Throws<ArgumentException>(() => OwnedCacheEntry.KeyedTemplate("widget-service", "widget", "ma:ker", null));
    }

    [Fact]
    public void ReadManyTemplate_NamesItsParty()
    {
        var template = OwnedCacheEntry.ReadManyTemplate("widget-service", "own-widget", "user");

        Assert.Equal("widget-service:own-widget:read-many:{user}", template);
        Assert.Equal($"widget-service:own-widget:read-many:{Id}", OwnedCacheEntry.Key(template, null, null, Id, null));
        Assert.Null(OwnedCacheEntry.Key(template, null, null, null, null));
    }

    [Theory]
    [InlineData("own-widget", "team")]
    [InlineData("own:widget", "user")]
    [InlineData("answer", "user")]
    public void ReadManyTemplate_AmbiguousNames_Refused(string resourceType, string party)
    {
        Assert.Throws<ArgumentException>(() => OwnedCacheEntry.ReadManyTemplate("widget-service", resourceType, party));
    }

    [Fact]
    public void Key_FillsThePartiesACallCarries()
    {
        var organization = Guid.Parse("b0000000-0000-4000-8000-000000000002");
        const string current = "widget-service:widget:by-maker:{organization}:{user}";

        Assert.Equal(
            $"widget-service:widget:by-maker:{organization}:{Id}",
            OwnedCacheEntry.Key(current, null, organization, Id, null));
        Assert.Equal(
            $"widget-service:widget:by-maker:{organization}:{Id}",
            OwnedCacheEntry.Key("widget-service:widget:by-maker:{organization}:{key}", Id, organization, null, null));

        // A placeholder the call doesn't fill is no key at all.
        Assert.Null(OwnedCacheEntry.Key(current, Id, organization, null, null));
        Assert.Null(OwnedCacheEntry.Key(current, null, Guid.Empty, Id, null));
    }

    [Fact]
    public void Fields_AreTheContract()
    {
        Assert.Equal("v", OwnedCacheEntry.Version);
        Assert.Equal("o", OwnedCacheEntry.Owner);
        Assert.Equal("d", OwnedCacheEntry.Data);
        Assert.Equal("f", OwnedCacheEntry.Format);
        Assert.Equal(1, OwnedCacheEntry.CurrentFormat);
        Assert.Equal("none", OwnedCacheEntry.None);
        Assert.Equal("gone", OwnedCacheEntry.Gone);
        Assert.Equal("x-bitwarden-cache", OwnedCacheEntry.Extension);
    }

    [Fact]
    public void SettingsAndPlaceholders_AreTheContract()
    {
        Assert.Equal("Caching:Owned:Redis:Configuration", OwnedCacheEntry.ConnectionKey);
        Assert.Equal("Caching:Owned:Resources", OwnedCacheEntry.ResourcesSection);
        Assert.Equal("{key}", OwnedCacheEntry.KeyPlaceholder);
        Assert.Equal("{organization}", OwnedCacheEntry.OrganizationPlaceholder);
        Assert.Equal("{user}", OwnedCacheEntry.UserPlaceholder);
        Assert.Equal("{provider}", OwnedCacheEntry.ProviderPlaceholder);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("provider")]
    public void KeyedTemplate_NamesEachOwnersKind(string ownerAxis)
    {
        Assert.Equal(
            $"widget-service:widget:by-maker:{{{ownerAxis}}}:{{key}}",
            OwnedCacheEntry.KeyedTemplate("widget-service", "widget", "maker", ownerAxis));
    }

    [Fact]
    public void Key_FillsTheProvider()
    {
        var provider = Guid.Parse("c0000000-0000-4000-8000-000000000003");
        var template = OwnedCacheEntry.KeyedTemplate("widget-service", "widget", "maker", "provider");

        Assert.Equal(
            $"widget-service:widget:by-maker:{provider}:{Id}",
            OwnedCacheEntry.Key(template, Id, null, null, provider));
        Assert.Null(OwnedCacheEntry.Key(template, Id, null, null, null));
    }

    // A template naming no placeholder this package fills is no key at all.
    [Fact]
    public void Key_AnUnknownPlaceholder_IsNull()
    {
        Assert.Null(OwnedCacheEntry.Key("widget-service:widget:{team}:{key}", Id, null, null, null));
    }
}
