using System.Text.Json;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// What every targeted bulk request shares: all-or-nothing unless the operation says otherwise,
/// and a caller cannot choose differently in the body.
/// </summary>
public sealed class BulkTargetingTests
{
    [Fact]
    public void Atomicity_ByDefault_IsAllOrNothing()
    {
        BulkTargeting<Widget> actual = new BulkUpdate<Widget> { Update = new Dictionary<string, JsonElement>() };

        Assert.Equal(Atomicity.AllOrNothing, actual.Atomicity);
    }

    [Fact]
    public void Read_AtomicityInTheBody_IsIgnored()
    {
        BulkTargeting<Widget> actual = JsonSerializer.Deserialize<BulkDelete<Widget>>(
            """{ "ids": ["a"], "atomicity": "BestEffort" }""", JsonSerializerOptions.Web)!;

        Assert.Equal(Atomicity.AllOrNothing, actual.Atomicity);
    }

    [Fact]
    public void Atomicity_SetByThePlatform_IsKept()
    {
        var actual = new BulkDelete<Widget> { Ids = ["a"] };

        actual.Atomicity = Atomicity.BestEffort;

        Assert.Equal(Atomicity.BestEffort, actual.Atomicity);
    }

    public sealed record Widget : Resource;
}
