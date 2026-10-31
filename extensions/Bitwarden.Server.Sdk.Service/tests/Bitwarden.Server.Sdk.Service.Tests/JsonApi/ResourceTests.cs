using System.Reflection;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// A resource derives its <c>type</c> from its own name, marks the audit members read-only, and
/// compares by value.
/// </summary>
public sealed class ResourceTests
{
    [Fact]
    public void Type_NoAttribute_IsTheRecordsNameInKebabCase()
    {
        Assert.Equal("api-key", new ApiKey().Type);
    }

    [Fact]
    public void Type_ResourceTypeAttribute_OverridesTheConvention()
    {
        Assert.Equal("gizmo", new Renamed().Type);
    }

    [Fact]
    public void New_NothingSet_EveryMemberButTypeIsAbsent()
    {
        var actual = new ApiKey();

        Assert.Null(actual.Id);
        Assert.Null(actual.Version);
        Assert.Null(actual.CreatedAt);
        Assert.Null(actual.CreatedBy);
        Assert.Null(actual.UpdatedAt);
        Assert.Null(actual.UpdatedBy);
        Assert.Null(actual.DeletedAt);
        Assert.Null(actual.DeletedBy);
    }

    // A caller may read who changed a resource and when, but only the server sets it.
    [Theory]
    [InlineData(nameof(Resource.CreatedAt))]
    [InlineData(nameof(Resource.CreatedBy))]
    [InlineData(nameof(Resource.UpdatedAt))]
    [InlineData(nameof(Resource.UpdatedBy))]
    [InlineData(nameof(Resource.DeletedAt))]
    [InlineData(nameof(Resource.DeletedBy))]
    public void AuditMember_IsReadOnly(string member)
    {
        Assert.True(typeof(Resource).GetProperty(member)!.IsDefined(typeof(ReadOnlyAttribute)));
    }

    // The identifier and version travel in requests: a replace names both.
    [Theory]
    [InlineData(nameof(Resource.Id))]
    [InlineData(nameof(Resource.Type))]
    [InlineData(nameof(Resource.Version))]
    public void IdentityMember_IsNotReadOnly(string member)
    {
        Assert.False(typeof(Resource).GetProperty(member)!.IsDefined(typeof(ReadOnlyAttribute)));
    }

    [Fact]
    public void Equals_SameValues_Equal()
    {
        Assert.Equal(new ApiKey { Id = "1", Label = "fake" }, new ApiKey { Id = "1", Label = "fake" });
        Assert.NotEqual(new ApiKey { Id = "1", Label = "fake" }, new ApiKey { Id = "2", Label = "fake" });
    }

    public sealed record ApiKey : Resource
    {
        public string? Label { get; init; }
    }

    [ResourceType("gizmo")]
    public sealed record Renamed : Resource;
}
