using System.Text.Json;
using Bitwarden.Server.Sdk.Common.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// An authentication or authorization failure answered as the standard error document, never a bare status; a success
/// passed on to the operation.
/// </summary>
public sealed class AuthorizationResultHandlerTests
{
    private static readonly AuthorizationPolicy Policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

    private static DefaultHttpContext Request() => new()
    {
        RequestServices = new ServiceCollection().BuildServiceProvider(),
        Response = { Body = new MemoryStream() },
    };

    private static async Task<JsonElement> Error(HttpContext http)
    {
        http.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(http.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        return Assert.Single(document.RootElement.GetProperty("errors").EnumerateArray()).Clone();
    }

    [Fact]
    public async Task Handle_Succeeded_RunsTheOperation()
    {
        var http = Request();
        var ran = false;

        await new AuthorizationResultHandler().HandleAsync(
            _ => { ran = true; return Task.CompletedTask; }, http, Policy, PolicyAuthorizationResult.Success());

        Assert.True(ran);
        Assert.Equal(0, http.Response.Body.Length);
    }

    [Fact]
    public async Task Handle_Challenged_Unauthenticated401()
    {
        var http = Request();

        await new AuthorizationResultHandler().HandleAsync(Unreached, http, Policy, PolicyAuthorizationResult.Challenge());

        Assert.Equal(StatusCodes.Status401Unauthorized, http.Response.StatusCode);
        Assert.StartsWith("application/json", http.Response.ContentType, StringComparison.Ordinal);
        var error = await Error(http);
        Assert.Equal("401", error.GetProperty("status").GetString());
        Assert.Equal(ErrorCodes.Unauthenticated, error.GetProperty("code").GetString());
        Assert.Equal(ErrorTitles.Unauthenticated, error.GetProperty("title").GetString());
        Assert.Equal(ErrorDetails.Unauthenticated, error.GetProperty("detail").GetString());
    }

    // The first reason a handler gave is the detail: it says which scope or header the operation needs.
    [Fact]
    public async Task Handle_ForbiddenWithAReason_Forbidden403SayingTheFirstReason()
    {
        var http = Request();
        var handler = new AnyScopeHandler(new NoContext());
        var failure = AuthorizationFailure.Failed(
        [
            new AuthorizationFailureReason(handler, "The 'vault.read' scope is required."),
            new AuthorizationFailureReason(handler, "A later reason."),
        ]);

        await new AuthorizationResultHandler().HandleAsync(Unreached, http, Policy, PolicyAuthorizationResult.Forbid(failure));

        Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
        var error = await Error(http);
        Assert.Equal("403", error.GetProperty("status").GetString());
        Assert.Equal(ErrorCodes.Forbidden, error.GetProperty("code").GetString());
        Assert.Equal(ErrorTitles.Forbidden, error.GetProperty("title").GetString());
        Assert.Equal("The 'vault.read' scope is required.", error.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Handle_ForbiddenWithoutAReason_Forbidden403WithTheStandardDetail()
    {
        var http = Request();

        await new AuthorizationResultHandler().HandleAsync(Unreached, http, Policy, PolicyAuthorizationResult.Forbid());

        Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
        Assert.Equal(ErrorDetails.Forbidden, (await Error(http)).GetProperty("detail").GetString());
    }

    private static Task Unreached(HttpContext http) => throw new InvalidOperationException("A refused request reached the operation.");

    private sealed class NoContext : Bitwarden.Server.Sdk.Common.IContext
    {
        public Guid? OrganizationId => null;

        public Guid? UserId => null;

        public Guid? ProviderId => null;
    }
}
