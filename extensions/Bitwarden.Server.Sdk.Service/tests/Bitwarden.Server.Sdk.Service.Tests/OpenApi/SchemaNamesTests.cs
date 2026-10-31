using System.Text.Json;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// A resource document's schema is named after the domain rather than the platform's generics,
/// query-bound types and enumerations get no schema of their own, and anything else keeps the
/// generator's name.
/// </summary>
public sealed class SchemaNamesTests
{
    /// <summary>
    /// The resource the documents carry.
    /// </summary>
    public sealed record Gadget : Resource;

    /// <summary>
    /// An enumeration, inlined wherever it is used.
    /// </summary>
    public enum Shade
    {
        /// <summary>
        /// Not a real colour.
        /// </summary>
        Red,
    }

    /// <summary>
    /// A generic type the platform does not name.
    /// </summary>
    public sealed class Box<T>
    {
        /// <summary>
        /// What is in it.
        /// </summary>
        public T? Content { get; init; }
    }

    [Theory]
    [InlineData(typeof(ResourceDocument<Gadget>), "GadgetDocument")]
    [InlineData(typeof(ResourceListDocument<Gadget>), "GadgetListDocument")]
    [InlineData(typeof(PagedResourceListDocument<Gadget>), "GadgetPagedListDocument")]
    [InlineData(typeof(CursorPagedResourceListDocument<Gadget>), "GadgetCursorListDocument")]
    [InlineData(typeof(BulkCreatedDocument<Gadget>), "GadgetBulkCreatedDocument")]
    public void For_Document_IsNamedAfterItsResource(Type type, string expected)
    {
        Assert.Equal(expected, SchemaNames.For(Info(type)));
    }

    [Theory]
    [InlineData(typeof(Page))]
    [InlineData(typeof(Sort))]
    [InlineData(typeof(PageNumber))]
    [InlineData(typeof(PageSize))]
    [InlineData(typeof(PageAfter))]
    [InlineData(typeof(Filter<Guid>))]
    [InlineData(typeof(Range<DateTimeOffset>))]
    public void For_QueryBoundType_HasNoSchemaOfItsOwn(Type type)
    {
        Assert.Null(SchemaNames.For(Info(type)));
    }

    [Theory]
    [InlineData(typeof(Shade))]
    [InlineData(typeof(Shade?))]
    public void For_Enumeration_IsInlined(Type type)
    {
        Assert.Null(SchemaNames.For(Info(type)));
    }

    [Theory]
    [InlineData(typeof(Gadget), "Gadget")]
    [InlineData(typeof(Box<Gadget>), "BoxOfGadget")]
    public void For_AnythingElse_KeepsTheGeneratorsName(Type type, string expected)
    {
        Assert.Equal(expected, SchemaNames.For(Info(type)));
    }

    private static System.Text.Json.Serialization.Metadata.JsonTypeInfo Info(Type type) =>
        JsonSerializerOptions.Web.GetTypeInfo(type);
}
