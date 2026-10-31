using System.Reflection;
using Bitwarden.Server.Sdk.Service.Endpoints;
using Microsoft.AspNetCore.Mvc.Abstractions;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// An operation's handler is the last method in its endpoint's metadata, and an operation without
/// one has none.
/// </summary>
public sealed class OperationMethodsTests
{
    private static readonly MethodInfo First = typeof(OperationMethodsTests).GetMethod(nameof(Of_OneMethod_IsIt))!;
    private static readonly MethodInfo Last = typeof(OperationMethodsTests).GetMethod(nameof(Of_SeveralMethods_TheLast))!;

    [Fact]
    public void Of_SeveralMethods_TheLast()
    {
        var action = new ActionDescriptor { EndpointMetadata = [First, "other", Last, new object()] };

        Assert.Same(Last, OperationMethods.Of(action));
    }

    [Fact]
    public void Of_NoMethod_Null()
    {
        Assert.Null(OperationMethods.Of(new ActionDescriptor { EndpointMetadata = ["other"] }));
    }

    [Fact]
    public void Of_NoMetadata_Null()
    {
        Assert.Null(OperationMethods.Of(new ActionDescriptor { EndpointMetadata = null! }));
    }

    [Fact]
    public void Of_OneMethod_IsIt()
    {
        Assert.Same(First, OperationMethods.Of(new ActionDescriptor { EndpointMetadata = [First] }));
    }
}
