using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// What <see cref="SortableAttribute"/> carries: ascending and not the default unless it says so,
/// where the field is stored, and on a resource the inherited field it names.
/// </summary>
public sealed class SortableAttributeTests
{
    [Fact]
    public void ByDefault_AscendingNotTheDefaultAndStoredUnderItsOwnName()
    {
        var actual = new SortableAttribute();

        Assert.Null(actual.Field);
        Assert.False(actual.Default);
        Assert.False(actual.Descending);
        Assert.Null(actual.MappedTo);
    }

    [Fact]
    public void OnAResource_CarriesEverythingItIsGiven()
    {
        var actual = new SortableAttribute("createdAt") { Default = true, Descending = true, MappedTo = "CreationDate" };

        Assert.Equal("createdAt", actual.Field);
        Assert.True(actual.Default);
        Assert.True(actual.Descending);
        Assert.Equal("CreationDate", actual.MappedTo);
    }

    [Fact]
    public void Usage_PropertiesAndClasses_ManyTimes()
    {
        var actual = Assert.Single(typeof(SortableAttribute).GetCustomAttributes(typeof(AttributeUsageAttribute), false).Cast<AttributeUsageAttribute>());

        Assert.Equal(AttributeTargets.Property | AttributeTargets.Class, actual.ValidOn);
        Assert.True(actual.AllowMultiple);
    }
}
