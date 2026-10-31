using System.Text.Json;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// The factory converts every closed <see cref="BulkInput{TInput}"/>, and nothing else.
/// </summary>
public sealed class BulkInputJsonConverterFactoryTests
{
    private readonly BulkInputJsonConverterFactory _factory = new();

    [Theory]
    [InlineData(typeof(BulkInput<int>), true)]
    [InlineData(typeof(BulkInput<string>), true)]
    [InlineData(typeof(List<int>), false)]
    [InlineData(typeof(string), false)]
    public void CanConvert_OnlyABulkInput(Type type, bool expected)
    {
        Assert.Equal(expected, _factory.CanConvert(type));
    }

    [Fact]
    public void CreateConverter_IsTheConverterForThatInput()
    {
        var actual = _factory.CreateConverter(typeof(BulkInput<string>), JsonSerializerOptions.Web);

        Assert.IsType<BulkInputJsonConverter<string>>(actual);
    }

    [Fact]
    public void CreateConverter_NoType_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _factory.CreateConverter(null!, JsonSerializerOptions.Web));
    }
}
