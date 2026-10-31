using Bitwarden.Server.Sdk.Common;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// An operation declared to create an organization may name one created organization on its outgoing calls, and
/// nothing else may.
/// </summary>
public sealed class OrganizationCreationTests
{
    private static readonly Guid Created = new("c1000000-0000-4000-8000-00000000000c");
    private static readonly Guid Other = new("c2000000-0000-4000-8000-00000000000c");

    [Fact]
    public void AnOperationThatCreatesOne_MayNameTheOrganizationItCreated_AgainAndAgain()
    {
        var creation = Creation(Request(creates: true));

        Assert.True(creation.Allows(Created));
        Assert.True(creation.Allows(Created));
    }

    [Fact]
    public void AnOperationThatCreatesOne_MayNotNameASecondOrganization()
    {
        var creation = Creation(Request(creates: true));

        Assert.True(creation.Allows(Created));
        Assert.False(creation.Allows(Other));
    }

    // The first organization named belongs to the request, not to the scope that named it.
    [Fact]
    public void ASecondOrganization_IsRefused_FromAnotherScopeOfTheSameRequest()
    {
        var http = Request(creates: true);

        Assert.True(Creation(http).Allows(Created));
        Assert.False(Creation(http).Allows(Other));
    }

    [Fact]
    public void AnOperationNotDeclaredToCreateOne_MayNotNameOne()
    {
        Assert.False(Creation(Request(creates: false)).Allows(Created));
    }

    [Fact]
    public void ARequestNamingAnOrganization_MayNotNameAnother()
    {
        Assert.False(Creation(Request(creates: true), new StatedContext { OrganizationId = Other }).Allows(Created));
    }

    [Fact]
    public void OutsideARequest_NothingIsAllowed()
    {
        Assert.False(new OrganizationCreation(new Accessor(null), new StatedContext()).Allows(Created));
    }

    // Calls made at once each name an organization: only one of them may be first.
    [Fact]
    public async Task CallsNamingDifferentOrganizationsAtOnce_OnlyOneIsAllowed()
    {
        const int calls = 8;

        for (var round = 0; round < 500; round++)
        {
            var http = Request(creates: true);
            using var start = new Barrier(calls);
            var allowed = await Task.WhenAll(Enumerable.Range(0, calls).Select(_ => Task.Run(() =>
            {
                var creation = Creation(http);
                start.SignalAndWait(TestContext.Current.CancellationToken);
                return creation.Allows(Guid.NewGuid());
            })));

            Assert.Equal(1, allowed.Count(allows => allows));
        }
    }

    [Fact]
    public void TheDeclaration_IsMadeOnceOnAnOperation()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(typeof(CreatesOrganizationAttribute), typeof(AttributeUsageAttribute))!;

        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }

    private static OrganizationCreation Creation(HttpContext http, IContext? context = null) =>
        new(new Accessor(http), context ?? new StatedContext());

    private static DefaultHttpContext Request(bool creates)
    {
        var http = new DefaultHttpContext();
        http.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            creates ? new EndpointMetadataCollection(new CreatesOrganizationAttribute()) : EndpointMetadataCollection.Empty,
            "operation"));
        return http;
    }

    /// <summary>
    /// The request being handled, held plainly rather than flowed with the async context.
    /// </summary>
    private sealed class Accessor(HttpContext? http) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = http;
    }
}
