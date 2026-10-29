using System.Reflection;
using Bitwarden.Server.Sdk.Common.Errors;

namespace Bitwarden.Server.Sdk.Common.Tests.Errors;

// A title names the kind of problem in one sentence; two kinds never share one.
public sealed class ErrorTitlesTests
{
    private static readonly string[] Titles = typeof(ErrorTitles)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral)
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToArray();

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
}
