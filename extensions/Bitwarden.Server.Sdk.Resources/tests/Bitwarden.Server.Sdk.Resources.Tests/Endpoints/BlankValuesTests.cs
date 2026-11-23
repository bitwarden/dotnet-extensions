using Bitwarden.Server.Sdk.Resources.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Resources.Tests.Endpoints;

/// <summary>
/// A <c>string</c> route, query or header value reaches the handler as an MVC controller's binder gives it: the first
/// a query sent, none for a blank one, or the parameter's default, and each blank value of a <c>string</c> array as
/// null in its place.
/// </summary>
public sealed class BlankValuesTests
{
    [Theory]
    [InlineData("")]
    [InlineData("%20")]
    [InlineData("%09%20")]
    public void BindAsNone_BlankQueryValue_None(string blank)
    {
        var arguments = Bind($"?q={blank}", route: "x", header: "x");

        Assert.Null(arguments[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void BindAsNone_BlankHeaderOrRouteValue_None(string blank)
    {
        var arguments = Bind("?q=x", route: blank, header: blank);

        Assert.Null(arguments[0]);
        Assert.Null(arguments[2]);
    }

    [Theory]
    [InlineData("?fallback=")]
    [InlineData("?fallback=%20")]
    public void BindAsNone_BlankWithADefault_TheDefault(string query)
    {
        var arguments = Bind(query, route: "x", header: "x");

        Assert.Equal("fallback", arguments[3]);
    }

    // As a controller and the framework's runtime factory give it, where the request delegate generator gives none.
    [Fact]
    public void BindAsNone_StringWithADefaultNotSent_TheDefault()
    {
        var arguments = Bind("?q=x", route: "x", header: "x");

        Assert.Equal("fallback", arguments[3]);
    }

    // The platform joins a query value sent more than once; a controller's binder takes the first.
    [Theory]
    [InlineData("?q=a&q=b", "a")]
    [InlineData("?q=a&q=", "a")]
    [InlineData("?q=&q=b", null)]
    public void BindAsNone_QueryValueSentTwice_TheFirst(string query, string? expected)
    {
        var arguments = Bind(query, route: "x", header: "x", given: "joined");

        Assert.Equal(expected, arguments[1]);
    }

    [Fact]
    public void BindAsNone_ArrayWithBlankValues_NullInTheirPlaces()
    {
        var arguments = Bind("?q=x", route: "x", header: "x", many: ["a", "", " ", "b"]);

        Assert.Equal(new string?[] { "a", null, null, "b" }, arguments[4]);
    }

    // Padding is part of a value that is not blank, as a controller's binder keeps it.
    [Fact]
    public void BindAsNone_NotBlank_AsSent()
    {
        string[] many = ["a", " b "];

        var arguments = Bind("?q=%20x%20", route: " y ", header: " z ", many: many);

        Assert.Equal(" y ", arguments[0]);
        Assert.Equal(" x ", arguments[1]);
        Assert.Equal(" z ", arguments[2]);
        Assert.Same(many, arguments[4]);
    }

    // A handler that answers invalid requests itself names what was sent in its refusal.
    [Fact]
    public void BindAsNone_RequiredBlankForAHandlerAcceptingInvalidRequests_AsSent()
    {
        var arguments = Bind("?q=%20&optional=%20", route: " ", header: "x", acceptsInvalid: true);

        Assert.Equal(" ", arguments[0]);
        Assert.Equal(" ", arguments[1]);
        Assert.Null(arguments[5]);
    }

    // Still the first value a query sent, as a controller binds it, not the platform's joined values.
    [Fact]
    public void BindAsNone_RequiredSentTwiceForAHandlerAcceptingInvalidRequests_TheFirst()
    {
        var arguments = Bind("?q=x&q=y", route: "x", header: "x", given: "x,y", acceptsInvalid: true);

        Assert.Equal("x", arguments[1]);
    }

    /// <summary>
    /// Binds <see cref="Handlers.Read"/>'s arguments for a request, each string given as the platform passes it.
    /// </summary>
    private static IList<object?> Bind(
        string query, string route, string header, string[]? many = null, string? given = null, bool acceptsInvalid = false)
    {
        var method = typeof(Handlers).GetMethod(nameof(Handlers.Read))!;
        var parameters = RequestParameters.For(method, new ServiceCollection().BuildServiceProvider(), []);
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString(query);
        http.Request.RouteValues["id"] = route;
        http.Request.Headers["X-Name"] = header;
        var sent = http.Request.Query;
        var context = new DefaultEndpointFilterInvocationContext(
            http,
            route,
            given ?? sent["q"].ToString(),
            header,
            sent.ContainsKey("fallback") ? sent["fallback"].ToString() : null,
            many,
            sent.ContainsKey("optional") ? sent["optional"].ToString() : null);

        BlankValues.BindAsNone(context, parameters, acceptsInvalid);

        return context.Arguments;
    }

    /// <summary>
    /// Handlers whose arguments are bound.
    /// </summary>
    public static class Handlers
    {
#pragma warning disable IDE0060
        /// <summary>
        /// Strings from every place, one with a default, an array and an optional one.
        /// </summary>
        public static void Read(
            string id,
            [FromQuery] string q,
            [FromHeader(Name = "X-Name")] string name,
            [FromQuery] string? fallback = "fallback",
            [FromQuery] string?[]? many = null,
            [FromQuery] string? optional = null)
        {
        }
#pragma warning restore IDE0060
    }
}
