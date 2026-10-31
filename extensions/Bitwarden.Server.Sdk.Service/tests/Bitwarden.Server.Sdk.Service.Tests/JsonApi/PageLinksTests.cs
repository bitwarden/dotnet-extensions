using System.Text.Json;
using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// The links of a page: a link that would go nowhere is absent from the wire, not null.
/// </summary>
public sealed class PageLinksTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void Serialize_EveryLink_InFirstPrevNextLastOrder()
    {
        var links = new PageLinks { First = "/w?page[number]=1", Prev = "/w?page[number]=1", Next = "/w?page[number]=3", Last = "/w?page[number]=4" };

        Assert.Equal(
            """{"first":"/w?page[number]=1","prev":"/w?page[number]=1","next":"/w?page[number]=3","last":"/w?page[number]=4"}""",
            JsonSerializer.Serialize(links, Options).Replace("\\u0026", "&", StringComparison.Ordinal));
    }

    [Fact]
    public void Serialize_OnlyPage_NoPrevOrNext()
    {
        var links = new PageLinks { First = "f", Last = "l" };

        Assert.Equal("""{"first":"f","last":"l"}""", JsonSerializer.Serialize(links, Options));
    }

    [Fact]
    public void Equals_SameLinks_Equal()
    {
        Assert.Equal(new PageLinks { First = "f" }, new PageLinks { First = "f" });
        Assert.NotEqual(new PageLinks { First = "f" }, new PageLinks { First = "f", Next = "n" });
    }
}
