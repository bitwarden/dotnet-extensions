using System.Net;
using System.Text.Json;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;

namespace Bitwarden.Server.Sdk.Service.Tests;

/// <summary>
/// <c>[RequireFeature]</c> holds an operation behind a flag on every service: <c>404</c> while the
/// flag is off, and only once the caller is authorized.
/// </summary>
/// <remarks>
/// Flags come from <c>Features:FlagValues</c>, served offline, so nothing leaves the process.
/// </remarks>
public sealed class FeatureCheckTests
{
    private const string Path = "/api/v1/probes/flagged";

    private static readonly Guid Person = new("c4000000-0000-4000-8000-00000000000d");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FlaggedOperation_FlagOn_Runs(bool productService)
    {
        await using var service = await SessionHost.StartAsync(productService, Flag("true"));
        using var request = Authorized(productService);

        using var response = await service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FlaggedOperation_FlagOff_NotFoundInTheStandardShape(bool productService)
    {
        await using var service = await SessionHost.StartAsync(productService, Flag("false"));
        using var request = Authorized(productService);

        using var response = await service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        // The same answer as an address that matches nothing, not a problem document.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var error = document.RootElement.GetProperty("errors")[0];
        Assert.Equal("404", error.GetProperty("status").GetString());
        Assert.Equal("not-found", error.GetProperty("code").GetString());
    }

    // A flag nobody configured is off.
    [Fact]
    public async Task FlaggedOperation_FlagUnset_NotFound()
    {
        await using var service = await SessionHost.StartAsync(productService: true);
        using var request = Authorized(productService: true);

        using var response = await service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Authorization first: a caller without a session learns nothing
    // about the flag.
    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public async Task FlaggedOperation_NoSession_Unauthorized(string flag)
    {
        await using var service = await SessionHost.StartAsync(productService: true, Flag(flag));

        using var response = await service.Client().GetAsync(Path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // An operation with no flag is untouched by the check.
    [Fact]
    public async Task UnflaggedOperation_FlagOff_Runs()
    {
        await using var service = await SessionHost.StartAsync(productService: true, Flag("false"));
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/current", SessionHost.Token(Person, SessionHost.Scope));

        using var response = await service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Dictionary<string, string?> Flag(string value) => new()
    {
        [$"Features:FlagValues:{Probes.FlaggedProbeFlag}"] = value,
    };

    /// <summary>
    /// A request the operation authorizes: a person's session on a product service, a service's
    /// call about a person on a domain service.
    /// </summary>
    private static HttpRequestMessage Authorized(bool productService)
    {
        if (productService)
        {
            return ProductServiceAttributeTests.Get(Path, SessionHost.Token(Person, SessionHost.Scope));
        }

        var request = ProductServiceAttributeTests.Get(Path, SessionHost.Token(null, SessionHost.Scope));
        request.Headers.Add(BitwardenHeaders.User, Person.ToString());
        return request;
    }
}
