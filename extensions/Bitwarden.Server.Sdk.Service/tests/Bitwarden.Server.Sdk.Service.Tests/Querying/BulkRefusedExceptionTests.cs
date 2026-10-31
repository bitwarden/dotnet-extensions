using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// An all-or-nothing bulk operation refused, carrying every item's error so the caller can fix
/// them all in one go, and saying nothing was changed.
/// </summary>
public sealed class BulkRefusedExceptionTests
{
    [Fact]
    public void Errors_AreEveryErrorInOrder()
    {
        var refused = new BulkRefusedException(
        [
            new() { Status = "422", Code = "invalid-value", Title = "Invalid value", Source = new ErrorSource { Pointer = "/data/0" } },
            new() { Status = "422", Code = "invalid-value", Title = "Invalid value", Source = new ErrorSource { Pointer = "/data/2" } },
        ]);

        Assert.Equal(["/data/0", "/data/2"], refused.Errors.Select(error => error.Source?.Pointer));
    }

    [Fact]
    public void Message_SaysNothingWasChanged()
    {
        Assert.Contains("nothing was changed", new BulkRefusedException([]).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IsAnErrorProvider()
    {
        Assert.IsAssignableFrom<IProvideErrors>(new BulkRefusedException([]));
    }
}
