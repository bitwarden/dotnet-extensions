using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// One sortable field as read from a resource's declarations: published under one name, stored
/// under another where the resource says so.
/// </summary>
public sealed class SortableFieldTests
{
    [Fact]
    public void ReadFromADeclaration_CarriesItsNameDirectionAndWhereItIsStored()
    {
        var actual = Assert.Single(SortableFields.Of(typeof(Item)));

        Assert.Equal(
            new SortableField("owner", typeof(Item).GetProperty(nameof(Item.Owner))!, true, SortDirection.Descending, "OwnerId"),
            actual);
    }

    private sealed record Item : Resource
    {
        [Sortable(Default = true, Descending = true, MappedTo = "OwnerId")]
        public string? Owner { get; init; }
    }
}
