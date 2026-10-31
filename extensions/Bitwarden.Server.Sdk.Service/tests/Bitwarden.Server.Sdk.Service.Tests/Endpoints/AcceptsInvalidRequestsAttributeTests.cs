using System.Reflection;
using Bitwarden.Server.Sdk.Service.Endpoints;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// The attribute lets a request that failed binding through to the handler, declared on the handler
/// or on its class, and nowhere else.
/// </summary>
public sealed class AcceptsInvalidRequestsAttributeTests
{
    [Fact]
    public void Usage_ClassesAndMethods()
    {
        var usage = typeof(AcceptsInvalidRequestsAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Class | AttributeTargets.Method, usage.ValidOn);
    }

    [Fact]
    public void OnTheHandler_AcceptsInvalid()
    {
        Assert.True(RequestBodyCheck.AcceptsInvalid(typeof(Strict).GetMethod(nameof(Strict.Lenient))!));
    }

    [Fact]
    public void OnTheClass_AppliesToEveryHandler()
    {
        Assert.True(RequestBodyCheck.AcceptsInvalid(typeof(LenientClass).GetMethod(nameof(LenientClass.Any))!));
    }

    [Fact]
    public void Absent_RefusesInvalid()
    {
        Assert.False(RequestBodyCheck.AcceptsInvalid(typeof(Strict).GetMethod(nameof(Strict.Plain))!));
    }

    private static class Strict
    {
        [AcceptsInvalidRequests]
        public static void Lenient()
        {
        }

        public static void Plain()
        {
        }
    }

    [AcceptsInvalidRequests]
    private static class LenientClass
    {
        public static void Any()
        {
        }
    }
}
