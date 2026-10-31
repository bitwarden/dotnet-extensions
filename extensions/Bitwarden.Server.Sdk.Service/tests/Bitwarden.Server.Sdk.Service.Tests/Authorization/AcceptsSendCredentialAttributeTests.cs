using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// [AcceptsSendCredential]: a marker on one operation, which is what the platform reads to accept a Send's token there.
/// </summary>
/// <remarks>
/// What the attribute lets a request do is pinned through the pipeline in <see cref="SendCredentialTests"/>.
/// </remarks>
public sealed class AcceptsSendCredentialAttributeTests
{
    [Fact]
    public void TheDeclaration_IsMadeOnceOnAnOperation()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(AcceptsSendCredentialAttribute), typeof(AttributeUsageAttribute))!;

        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnTheEndpoint_IsWhatDeclaresTheOperationAcceptsIt(bool declared)
    {
        var http = new DefaultHttpContext();
        http.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            declared ? new EndpointMetadataCollection(new AcceptsSendCredentialAttribute()) : EndpointMetadataCollection.Empty,
            "operation"));

        Assert.Equal(declared, SendCredential.DeclaredBy(http));
    }

    [Fact]
    public void NoEndpoint_DeclaresNothing()
    {
        Assert.False(SendCredential.DeclaredBy(new DefaultHttpContext()));
    }
}
