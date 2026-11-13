namespace Bitwarden.Server.Sdk.Sagas.Tests;

public sealed class SpanDeclarationAttributeTests
{
    [Fact]
    public void AnAdmittedDefect_KeepsItsReason()
    {
        Assert.Equal("why", new SpansServicesUnhandledAttribute("why").Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void AShape_WithoutAReason_Throws(string reason)
    {
        var exception = Assert.Throws<ArgumentException>(() => new SpansServicesAttribute(SpanShape.RetrySafeByState, reason));

        Assert.Equal("reason", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void AnAdmittedDefect_WithoutAReason_Throws(string reason)
    {
        var exception = Assert.Throws<ArgumentException>(() => new SpansServicesUnhandledAttribute(reason));

        Assert.Equal("reason", exception.ParamName);
    }
}
