using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A fieldset naming an attribute the resource does not have: a 422 at the fieldset's parameter
/// naming the attribute.
/// </summary>
public sealed class InvalidFieldsExceptionTests
{
    [Fact]
    public void Errors_AreOne422AtTheParameter_NamingTheAttribute()
    {
        var refused = new InvalidFieldsException("fields[widget]", "nope");

        var actual = Assert.Single(refused.Errors);

        Assert.Equal("422", actual.Status);
        Assert.Equal(ErrorCodes.InvalidValue, actual.Code);
        Assert.Equal(ErrorTitles.InvalidValue, actual.Title);
        Assert.Equal("fields[widget]", actual.Source!.Parameter);
        Assert.Equal("The 'fields[widget]' parameter names 'nope', which is not one of the resource's attributes.", actual.Detail);
        Assert.Equal(actual.Detail, refused.Message);
    }
}
