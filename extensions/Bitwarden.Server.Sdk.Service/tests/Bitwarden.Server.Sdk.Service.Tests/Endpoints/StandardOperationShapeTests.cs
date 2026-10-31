using Bitwarden.Server.Sdk.Service.Endpoints;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Microsoft.AspNetCore.Mvc;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// What an operation's attribute settles: the status it succeeds with, the response type that
/// reaches the wire, and the version suffix and casing of its identifier.
/// </summary>
public sealed class StandardOperationShapeTests
{
    // An action is the one kind whose success varies with whether it has anything to say back.
    [Fact]
    public void SuccessStatus_ActionWithNothingToReturn_NoContent()
    {
        Assert.Equal(204, StandardOperationShape.SuccessStatus(new StandardActionAttribute("mark"), null));
    }

    [Fact]
    public void SuccessStatus_ActionWithAResponse_Ok()
    {
        Assert.Equal(200, StandardOperationShape.SuccessStatus(new StandardActionAttribute("mark"), typeof(string)));
    }

    [Fact]
    public void SuccessStatus_AnyOtherKind_ItsOwn()
    {
        Assert.Equal(201, StandardOperationShape.SuccessStatus(new StandardCreateAttribute(), null));
        Assert.Equal(204, StandardOperationShape.SuccessStatus(new StandardDeleteAttribute(), typeof(string)));
    }

    [Theory]
    [InlineData(typeof(void))]
    [InlineData(typeof(Task))]
    [InlineData(typeof(ValueTask))]
    [InlineData(typeof(IActionResult))]
    [InlineData(typeof(OkResult))]
    [InlineData(typeof(Task<IActionResult>))]
    public void UnwrapReturnType_NoValue_Nothing(Type returnType)
    {
        Assert.Null(StandardOperationShape.UnwrapReturnType(returnType));
    }

    [Theory]
    [InlineData(typeof(ResourceDocument<Shaped>))]
    [InlineData(typeof(Task<ResourceDocument<Shaped>>))]
    [InlineData(typeof(ValueTask<ResourceDocument<Shaped>>))]
    [InlineData(typeof(ActionResult<ResourceDocument<Shaped>>))]
    [InlineData(typeof(Task<ActionResult<ResourceDocument<Shaped>>>))]
    public void UnwrapReturnType_Wrapped_TheTypeOnTheWire(Type returnType)
    {
        Assert.Equal(typeof(ResourceDocument<Shaped>), StandardOperationShape.UnwrapReturnType(returnType));
    }

    // A generic type that is not a wrapper is itself the response.
    [Fact]
    public void UnwrapReturnType_OtherGeneric_Kept()
    {
        Assert.Equal(typeof(List<string>), StandardOperationShape.UnwrapReturnType(typeof(List<string>)));
    }

    [Theory]
    [InlineData("api/v1/users", "")]
    [InlineData("api/v2/users", "V2")]
    [InlineData("v3", "V3")]
    [InlineData("api/v12/users/{id}", "V12")]
    [InlineData("api/users", "")]
    [InlineData("api/v2beta/users", "")]
    [InlineData("api/xv2/users", "")]
    public void VersionSuffix_Template_FirstVersionUnmarked(string template, string expected)
    {
        Assert.Equal(expected, StandardOperationShape.VersionSuffix([template]));
    }

    [Fact]
    public void VersionSuffix_SeveralTemplates_TheFirstVersioned()
    {
        Assert.Equal("V3", StandardOperationShape.VersionSuffix([null, "health", "api/v3/users", "api/v4/users"]));
    }

    [Fact]
    public void VersionSuffix_NoTemplates_Empty()
    {
        Assert.Equal(string.Empty, StandardOperationShape.VersionSuffix([]));
    }

    [Theory]
    [InlineData("CreateUser", "createUser")]
    [InlineData("createUser", "createUser")]
    [InlineData("X", "x")]
    [InlineData("", "")]
    public void CamelCase_Name_FirstLetterLowered(string name, string expected)
    {
        Assert.Equal(expected, StandardOperationShape.CamelCase(name));
    }

    /// <summary>
    /// A resource for the response types.
    /// </summary>
    public sealed record Shaped : Resource;
}
