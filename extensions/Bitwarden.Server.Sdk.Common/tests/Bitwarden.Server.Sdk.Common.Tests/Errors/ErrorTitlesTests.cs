using System.Reflection;
using Bitwarden.Server.Sdk.Common.Errors;

namespace Bitwarden.Server.Sdk.Common.Tests.Errors;

public sealed class ErrorTitlesTests
{
    private static readonly string[] Titles = Constants(typeof(ErrorTitles)).Select(field => (string)field.GetRawConstantValue()!).ToArray();

    [Fact]
    public void Titles_AreSentences()
    {
        Assert.NotEmpty(Titles);
        Assert.All(Titles, title =>
        {
            Assert.True(char.IsUpper(title[0]), title);
            Assert.EndsWith(".", title);
        });
    }

    [Fact]
    public void Titles_AreDistinct()
    {
        Assert.Equal(Titles.Length, Titles.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Titles_CoverEveryErrorCode()
    {
        var codes = Constants(typeof(ErrorCodes)).Select(field => field.Name).Order(StringComparer.Ordinal);
        var titles = Constants(typeof(ErrorTitles)).Select(field => field.Name).Order(StringComparer.Ordinal);

        Assert.Equal(codes, titles);
    }

    private static IEnumerable<FieldInfo> Constants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.IsLiteral);
}
