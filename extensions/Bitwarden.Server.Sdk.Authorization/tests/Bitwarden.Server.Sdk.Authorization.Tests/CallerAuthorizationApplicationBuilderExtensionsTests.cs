using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Authorization.Tests;

/// <summary>
/// Each extension adds its middleware to the pipeline.
/// </summary>
public sealed class CallerAuthorizationApplicationBuilderExtensionsTests
{
    private static readonly Guid Organization = new("a0000000-0000-4000-8000-0000000000aa");
    private static readonly Guid Person = new("a1000000-0000-4000-8000-00000000000a");

    // The pipeline is built when the service starts, so a product service missing its standing doesn't start.
    [Fact]
    public void UseCurrentStanding_NoCurrentStandingRegistered_PipelineFailsToBuild()
    {
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());

        app.UseCurrentStanding();

        var failure = Assert.Throws<InvalidOperationException>(() => app.Build());
        Assert.Contains(nameof(ICurrentStanding), failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UseCurrentStanding_ResolvesTheStandingBeforeTheRestOfThePipeline()
    {
        var services = new ServiceCollection().AddSingleton<ICurrentStanding, OwnerEverywhere>().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        CallerStanding? seen = null;

        app.UseCurrentStanding();
        app.Run(http =>
        {
            seen = CallerStanding.Of(http);
            return Task.CompletedTask;
        });

        var http = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(CurrentContext.SubjectClaim, Person.ToString()),
                    new Claim(CallerRole.OrganizationClaim, Organization.ToString()),
                ],
                "Bearer")),
        };
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(), "GetWidget"));

        await app.Build()(http);

        Assert.Equal(OrganizationRole.Owner, seen?.Role);
    }

    private sealed class OwnerEverywhere : ICurrentStanding
    {
        public Task<MemberStanding?> MembershipAsync(Guid person, Guid organization, CancellationToken cancellationToken = default) =>
            Task.FromResult<MemberStanding?>(MemberStanding.Of(OrganizationRole.Owner));

        public Task<Guid?> ProviderOfAsync(Guid organization, CancellationToken cancellationToken = default) =>
            Task.FromResult<Guid?>(null);

        public Task<ProviderRole?> ProviderRoleAsync(Guid person, Guid provider, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProviderRole?>(null);
    }
}
