using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A cursor that cannot be continued from: a 422 at <c>page[after]</c> that says why, tells the
/// caller to start again, and quotes no more of the cursor than it needs to.
/// </summary>
public sealed class InvalidCursorExceptionTests
{
    [Fact]
    public void Errors_AreOne422AtTheParameter_SayingWhyAndWhatToDo()
    {
        var actual = Assert.Single(new InvalidCursorException("abc", "it was not issued by this operation.").Errors);

        Assert.Equal("422", actual.Status);
        Assert.Equal(ErrorCodes.InvalidValue, actual.Code);
        Assert.Equal(ErrorTitles.InvalidValue, actual.Title);
        Assert.Equal("page[after]", actual.Source!.Parameter);
        Assert.Equal(
            "'page[after]' is not a cursor this operation can continue from: it was not issued by this operation. "
            + "Start again without it. ('abc')",
            actual.Detail);
    }

    [Fact]
    public void Errors_ALongCursor_IsQuotedOnlyToItsFirstFortyCharacters()
    {
        var cursor = new string('a', 40) + "TAIL";

        var actual = Assert.Single(new InvalidCursorException(cursor, "reason.").Errors).Detail!;

        Assert.Contains($"('{new string('a', 40)}…')", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("TAIL", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void Errors_ACursorOfFortyCharacters_IsQuotedWhole()
    {
        var cursor = new string('b', 40);

        Assert.Contains($"('{cursor}')", Assert.Single(new InvalidCursorException(cursor, "reason.").Errors).Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public void Message_DoesNotQuoteTheCursor()
    {
        var actual = new InvalidCursorException("secretish", "reason.");

        Assert.Equal("'page[after]' cannot be used: reason.", actual.Message);
    }
}
