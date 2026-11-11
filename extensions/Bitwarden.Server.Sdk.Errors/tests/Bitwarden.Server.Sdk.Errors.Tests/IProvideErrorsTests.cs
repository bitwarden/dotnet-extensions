namespace Bitwarden.Server.Sdk.Errors.Tests;

public sealed class IProvideErrorsTests
{
    [Fact]
    public void StatusCode_WithNoneOfItsOwn_Is422()
    {
        IProvideErrors provider = new SomeErrors();

        Assert.Equal(422, provider.StatusCode);
        Assert.Single(provider.Errors);
    }

    private sealed class SomeErrors : IProvideErrors
    {
        public IReadOnlyList<Error> Errors { get; } = [new Error { Code = ErrorCodes.InvalidValue }];
    }
}
