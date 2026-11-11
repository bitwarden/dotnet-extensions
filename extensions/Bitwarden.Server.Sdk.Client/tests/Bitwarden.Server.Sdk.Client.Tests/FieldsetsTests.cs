using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// A fieldset naming every attribute a generated contract publishes, under the names it publishes them.
/// </summary>
public sealed class FieldsetsTests
{
    [Fact]
    public void Every_NewtonsoftContract_ItsPublishedNames()
    {
        Assert.Equal("email,usage", Fieldsets.Every<NewtonsoftAttributes>());
    }

    [Fact]
    public void Every_SystemTextJsonContract_ItsPublishedNames()
    {
        Assert.Equal("role,key", Fieldsets.Every<SystemTextJsonAttributes>());
    }

    [Fact]
    public void EveryExcept_LeavesOutTheNamed()
    {
        Assert.Equal("email", Fieldsets.EveryExcept<NewtonsoftAttributes>("usage"));
    }

    [Fact]
    public void EveryExcept_ANameTheContractDoesNotPublish_Refused()
    {
        Assert.Throws<ArgumentException>(() => Fieldsets.EveryExcept<NewtonsoftAttributes>("Usage"));
    }

    [Fact]
    public void Only_TheWanted()
    {
        Assert.Equal(["usage"], Fieldsets.Only<RequiringAttributes>("usage"));
    }

    [Fact]
    public void ForService_TheWantedAndTheRequired()
    {
        Assert.Equal("usage,organization", Fieldsets.ForService<RequiringAttributes>(["usage"]));
    }

    [Fact]
    public void Only_ANameTheContractDoesNotPublish_Refused()
    {
        Assert.Throws<ArgumentException>(() => Fieldsets.Only<RequiringAttributes>("nope"));
    }

    /// <summary>
    /// Shaped as NSwag generates with Newtonsoft: an attribute recognized by its name.
    /// </summary>
    private sealed class NewtonsoftAttributes
    {
        [JsonProperty("email")]
        public string? Email { get; set; }


        [JsonProperty("usage")]
        public bool? Usage { get; set; }

        // Not part of the contract: no published name.
        public IDictionary<string, object>? AdditionalProperties { get; set; }
    }

    /// <summary>
    /// A contract marking one attribute required, as NSwag does with <c>Required.Always</c>.
    /// </summary>
    private sealed class RequiringAttributes
    {
        [JsonProperty("email")]
        public string? Email { get; set; }

        [JsonProperty("organization", Required = 2)]
        public string? Organization { get; set; }

        [JsonProperty("usage")]
        public bool? Usage { get; set; }
    }

    private sealed class SystemTextJsonAttributes
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("key")]
        public string? Key { get; set; }
    }
}
