using System.Collections;
using Bitwarden.Server.Sdk.Service.Observability;

namespace Bitwarden.Server.Sdk.Service.Tests.Observability;

/// <summary>
/// A log scope's fields: read afresh each time, so a field noted after the scope opened is in it, and printed as
/// <c>key:value</c> pairs, not as a type name, by a formatter that calls <see cref="object.ToString"/>.
/// </summary>
public sealed class ContextFieldsTests
{
    [Fact]
    public void ToString_PrintsThePairs()
    {
        var fields = new ContextFields(() => [new("ServiceName", "widgets"), new("UserId", "u-1")]);

        Assert.Equal("ServiceName:widgets UserId:u-1", fields.ToString());
    }

    [Fact]
    public void NoFields_PrintNothing() =>
        Assert.Equal(string.Empty, new ContextFields(() => []).ToString());

    // A structured provider reads the pairs: by index, by count and by enumeration, both generic and not.
    [Fact]
    public void ThePairs_ReadAsAList()
    {
        var fields = new ContextFields(() => [new("ServiceName", "widgets"), new("UserId", "u-1")]);

        Assert.Equal(2, fields.Count);
        Assert.Equal(new KeyValuePair<string, object>("UserId", "u-1"), fields[1]);
        Assert.Equal(["ServiceName", "UserId"], fields.Select(field => field.Key));

        List<string> keys = [];
        var enumerator = ((IEnumerable)fields).GetEnumerator();
        while (enumerator.MoveNext())
        {
            keys.Add(((KeyValuePair<string, object>)enumerator.Current).Key);
        }

        Assert.Equal(["ServiceName", "UserId"], keys);
    }

    // The scope opens before the request is through: a field noted later is read when the log line is written.
    [Fact]
    public void AFieldNotedLater_IsRead()
    {
        List<KeyValuePair<string, object>> noted = [new("ServiceName", "widgets")];
        var fields = new ContextFields(() => [.. noted]);

        noted.Add(new("UserId", "u-1"));

        Assert.Equal(2, fields.Count);
        Assert.Equal("ServiceName:widgets UserId:u-1", fields.ToString());
    }
}
