using Bitwarden.Server.Sdk.Service.Endpoints;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A handler's parameter is described as a controller action's is, so what reads a controller's
/// parameter (its <see cref="System.Reflection.ParameterInfo"/>, for documentation and attributes)
/// reads a handler's.
/// </summary>
public sealed class HandlerParameterDescriptorTests
{
    [Fact]
    public void ParameterInfo_ReadAsAControllerActionsIs()
    {
        var parameter = typeof(HandlerParameterDescriptorTests).GetMethod(nameof(Handler), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetParameters()[0];

        var actual = new HandlerParameterDescriptor
        {
            Name = "id",
            ParameterType = typeof(string),
            ParameterInfo = parameter,
        };

        Assert.Same(parameter, Assert.IsAssignableFrom<IParameterInfoParameterDescriptor>(actual).ParameterInfo);
        Assert.Equal("id", actual.Name);
    }

    private static void Handler(string id) => _ = id;
}
