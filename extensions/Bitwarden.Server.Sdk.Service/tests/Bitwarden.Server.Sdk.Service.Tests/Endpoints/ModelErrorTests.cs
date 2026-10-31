using Bitwarden.Server.Sdk.Service.Endpoints;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A model error is a value keyed as a controller's model state keys it: the same key and message
/// are the same error, which is how a failure found twice is reported once.
/// </summary>
public sealed class ModelErrorTests
{
    [Fact]
    public void Equality_ByValue()
    {
        Assert.Equal(new ModelError("Data.Email", "Required."), new ModelError("Data.Email", "Required."));
        Assert.NotEqual(new ModelError("Data.Email", "Required."), new ModelError("Data.Name", "Required."));
    }

    [Fact]
    public void Distinct_SameFailureTwice_Once()
    {
        ModelError[] errors = [new("Items[1].Name", "Bad."), new("Items[1].Name", "Bad.")];

        Assert.Single(errors.Distinct());
    }
}
