using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// <c>fields[type]</c> read from the query: absent means every attribute, the last value wins, and an
/// attribute the resource does not have is refused as the caller's mistake.
/// </summary>
public sealed class FieldsBindingTests
{
    [Fact]
    public void Bind_Absent_IsEveryAttribute()
    {
        var actual = (Fields<Widget>)FieldsBinding.Bind(typeof(Fields<Widget>), new QueryCollection());

        Assert.Null(actual.Names);
        Assert.True(actual.Includes("name"));
    }

    [Fact]
    public void Bind_CommaSeparated_IsThoseAttributes_TrimmedAndWithoutEmpties()
    {
        var actual = (Fields<Widget>)FieldsBinding.Bind(typeof(Fields<Widget>), Query(" name , ,notes"));

        Assert.Equal(["name", "notes"], actual.Names!.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Bind_SentTwice_TakesTheLast()
    {
        var actual = (Fields<Widget>)FieldsBinding.Bind(typeof(Fields<Widget>), Query("name", "notes"));

        Assert.Equal(["notes"], actual.Names!);
    }

    // Sent but empty asks for no attributes, which is not the same as not asking.
    [Fact]
    public void Bind_Empty_IsNoAttributes()
    {
        var actual = (Fields<Widget>)FieldsBinding.Bind(typeof(Fields<Widget>), Query(string.Empty));

        Assert.Empty(actual.Names!);
        Assert.False(actual.Includes("name"));
    }

    // Unwrapped from the reflection that built it, so the caller gets a 422 rather than a 500.
    [Fact]
    public void Bind_UnknownAttribute_IsRefusedAsItself()
    {
        var actual = Assert.Throws<InvalidFieldsException>(() => FieldsBinding.Bind(typeof(Fields<Widget>), Query("name,nope")));

        Assert.Contains("'nope'", actual.Message, StringComparison.Ordinal);
    }

    private static QueryCollection Query(params string[] values) =>
        new(new Dictionary<string, StringValues> { [Fields<Widget>.Parameter] = values });

    public sealed record Widget : Resource
    {
        public string? Name { get; init; }

        public string? Notes { get; init; }
    }
}
