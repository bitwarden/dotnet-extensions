namespace Bitwarden.Server.Sdk.Sagas.Tests;

public sealed class SpansServicesAttributeTests
{
    [Fact]
    public void ADeclaration_KeepsItsShapes()
    {
        var declared = new SpansServicesAttribute(SpanShape.Compensated | SpanShape.IrreversibleLast, "why");

        Assert.Equal(SpanShape.Compensated | SpanShape.IrreversibleLast, declared.Shape);
        Assert.Equal("why", declared.Reason);
    }

    [Theory]
    [InlineData(SpanShape.None)]
    [InlineData((SpanShape)64)]
    [InlineData(SpanShape.Compensated | (SpanShape)64)]
    public void ADeclaration_WithoutAKnownShape_Throws(SpanShape shape)
    {
        var exception = Assert.Throws<ArgumentException>(() => new SpansServicesAttribute(shape, "why"));

        Assert.Equal("shape", exception.ParamName);
    }
}
