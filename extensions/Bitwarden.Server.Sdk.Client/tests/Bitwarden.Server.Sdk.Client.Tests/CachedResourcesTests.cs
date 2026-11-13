namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// A cached resource read into its generated type leniently: it holds only the attributes asked for, so one the
/// contract requires may be absent.
/// </summary>
public sealed class CachedResourcesTests
{
    [Fact]
    public void Read_WithoutAnAttributeTheContractRequires_ReadsWhatIsThere()
    {
        var widget = CachedResources.Read<Widget>("""{"color":"teal"}""");

        Assert.NotNull(widget);
        Assert.Equal("teal", widget.Color);
        Assert.Null(widget.Name);
    }

    [Fact]
    public void Read_NotJson_IsNull()
    {
        Assert.Null(CachedResources.Read<Widget>("not json"));
    }

    [Fact]
    public void As_AHit_ReadsTheResource()
    {
        Assert.Equal("red", CachedAnswer.Hit("""{"color":"red"}""").As<Widget>()?.Color);
    }

    [Fact]
    public void As_AnythingButAHit_IsNull()
    {
        Assert.Null(CachedAnswer.Miss.As<Widget>());
        Assert.Null(CachedAnswer.NothingRecorded.As<Widget>());
    }

    /// <summary>
    /// Shaped as NSwag generates a resource with Newtonsoft: one attribute the contract requires.
    /// </summary>
    private sealed class Widget
    {
        [Newtonsoft.Json.JsonProperty("name", Required = Newtonsoft.Json.Required.Always)]
        public string? Name { get; set; }

        [Newtonsoft.Json.JsonProperty("color")]
        public string? Color { get; set; }
    }
}
