using Bitwarden.Server.Sdk.Common.Errors;

namespace Bitwarden.Server.Sdk.Common.Tests.Errors;

// The details a caller reads name what was wrong, in the caller's own terms.
public sealed class ErrorDetailsTests
{
    [Fact]
    public void MethodNotAllowed_NamesTheMethod()
    {
        Assert.Equal("The resource does not support PATCH requests.", ErrorDetails.MethodNotAllowed("PATCH"));
    }

    [Fact]
    public void FieldRequired_NamesTheField()
    {
        Assert.Equal("The 'name' field is required.", ErrorDetails.FieldRequired("name"));
    }

    [Fact]
    public void NotAPermittedValue_ListsWhatIs()
    {
        Assert.Equal("The 'kind' field must be one of: a, b.", ErrorDetails.NotAPermittedValue("kind", ["a", "b"]));
    }

    [Fact]
    public void InvalidType_SaysWhatWasExpected()
    {
        Assert.Equal("The 'count' field must be a number.", ErrorDetails.InvalidType("count", "a number"));
    }

    [Fact]
    public void NotFoundById_NamesTypeAndId()
    {
        Assert.Equal("No widget with ID '42' exists.", ErrorDetails.NotFoundById("widget", "42"));
    }

    [Fact]
    public void AlreadyExists_NamesTheField()
    {
        Assert.Equal("The 'email' value is already in use.", ErrorDetails.AlreadyExists("email"));
    }

    [Fact]
    public void InvalidResourceType_NamesBothTypes()
    {
        Assert.Equal(
            "A resource of type 'widget' was expected, but 'gadget' was declared.",
            ErrorDetails.InvalidResourceType("widget", "gadget"));
    }
}
