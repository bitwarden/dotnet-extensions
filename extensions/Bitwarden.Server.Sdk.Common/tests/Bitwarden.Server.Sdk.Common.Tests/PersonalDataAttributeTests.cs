using System.Reflection;

namespace Bitwarden.Server.Sdk.Common.Tests;

// A marker on a resource's properties, read where a copy could reach more readers than the resource does.
public sealed class PersonalDataAttributeTests
{
    [Fact]
    public void Attribute_MarksPropertiesOnly_Once()
    {
        var usage = typeof(PersonalDataAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Property, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }

    [Fact]
    public void Attribute_IsReadFromAMarkedProperty()
    {
        Assert.NotNull(typeof(Person).GetProperty(nameof(Person.Address))!.GetCustomAttribute<PersonalDataAttribute>());
        Assert.Null(typeof(Person).GetProperty(nameof(Person.Id))!.GetCustomAttribute<PersonalDataAttribute>());
    }

    private sealed class Person
    {
        public Guid Id { get; init; }

        [PersonalData]
        public string? Address { get; init; }
    }
}
