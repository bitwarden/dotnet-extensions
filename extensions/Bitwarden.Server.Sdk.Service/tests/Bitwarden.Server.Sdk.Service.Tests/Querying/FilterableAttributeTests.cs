using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// What <see cref="FilterableAttribute"/> carries: on a property, nothing but where it is stored; on
/// a resource, the inherited field it names, once per field.
/// </summary>
public sealed class FilterableAttributeTests
{
    [Fact]
    public void OnAProperty_NamesNoField()
    {
        var actual = new FilterableAttribute();

        Assert.Null(actual.Field);
        Assert.Null(actual.MappedTo);
    }

    [Fact]
    public void OnAResource_NamesTheFieldAndWhereItIsStored()
    {
        var actual = new FilterableAttribute("createdAt") { MappedTo = "CreationDate" };

        Assert.Equal("createdAt", actual.Field);
        Assert.Equal("CreationDate", actual.MappedTo);
    }

    // A resource names each inherited field with its own attribute.
    [Fact]
    public void Usage_PropertiesAndClasses_ManyTimes()
    {
        var actual = Assert.Single(typeof(FilterableAttribute).GetCustomAttributes(typeof(AttributeUsageAttribute), false).Cast<AttributeUsageAttribute>());

        Assert.Equal(AttributeTargets.Property | AttributeTargets.Class, actual.ValidOn);
        Assert.True(actual.AllowMultiple);
    }
}
