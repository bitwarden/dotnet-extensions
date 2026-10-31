using System.Reflection;
using Bitwarden.Server.Sdk.Service.Endpoints;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A classified parameter reports its type without <see cref="Nullable{T}"/>, and is called by its
/// own name in messages, or by the name it is sent under when it has none.
/// </summary>
public sealed class RequestParameterTests
{
    private static readonly MethodInfo Method =
        typeof(RequestParameterTests).GetMethod(nameof(Handler), BindingFlags.NonPublic | BindingFlags.Static)!;

    [Theory]
    [InlineData(0, typeof(int))]
    [InlineData(1, typeof(int))]
    [InlineData(2, typeof(string))]
    public void Underlying_NullableUnwrapped(int index, Type expected)
    {
        var actual = Of(Method.GetParameters()[index], "x");

        Assert.Equal(expected, actual.Underlying);
    }

    [Fact]
    public void DisplayName_IsTheParametersOwnName()
    {
        Assert.Equal("count", Of(Method.GetParameters()[0], "page[count]").DisplayName);
    }

    [Fact]
    public void DisplayName_ParameterWithoutAName_IsTheModelName()
    {
        Assert.Equal("value", Of(Method.ReturnParameter, "value").DisplayName);
    }

    private static RequestParameter Of(ParameterInfo info, string modelName) =>
        new(0, info, ParameterKind.Simple, modelName, ValuePlace.Query, Required: false);

    private static int Handler(int count, int? limit, string? name) => count + (limit ?? 0) + (name?.Length ?? 0);
}
