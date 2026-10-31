using System.Collections.Concurrent;
using System.Net;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Service.Tests;

/// <summary>
/// What every service does by default, whatever kind of service it is: its bare address leads to its
/// API documentation, and its log leaves out a request's query string.
/// </summary>
/// <remarks>
/// <para>
/// Without the redirect, <c>GET /</c> meets the fallback policy and answers <c>401</c>, which tells a
/// person opening the address in a browser nothing. The redirect is relative so that behind a proxy
/// the browser stays on the address it used.
/// </para>
/// <para>
/// ASP.NET's request lines carry the query string, and a query string can carry a credential, such
/// as a sign-in or download token. The platform defaults the one category that writes the URL to
/// <c>Warning</c> for every service, and a service that wants the lines opens it.
/// </para>
/// </remarks>
public sealed class ServiceTests
{
    private const string DocumentationIndex = "/docs/index.html";
    private const string Credential = "a-credential-carried-in-the-query";

    // No token at all: the redirect must not depend on one, and must not meet the fallback policy.
    [Theory]
    [InlineData(false, "GET")]
    [InlineData(false, "HEAD")]
    [InlineData(true, "GET")]
    [InlineData(true, "HEAD")]
    public async Task Root_Unauthenticated_RedirectsToTheDocumentation(bool productService, string method)
    {
        await using var service = await SessionHost.StartAsync(productService);
        using var request = new HttpRequestMessage(new HttpMethod(method), "/");

        using var response = await service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.False(response.Headers.Location.IsAbsoluteUri, "The Location must be relative.");
        Assert.Equal(DocumentationIndex, response.Headers.Location.OriginalString);
    }

    // The redirect points at something that is there.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentationIndex_Unauthenticated_IsServed(bool productService)
    {
        await using var service = await SessionHost.StartAsync(productService);

        using var response = await service.Client().GetAsync(DocumentationIndex, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // A convenience of the platform's, not part of any service's API.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenApi_Root_IsNotDocumented(bool productService)
    {
        await using var service = await SessionHost.StartAsync(productService);

        using var document = await ProductServiceAttributeTests.OpenApi(service);

        var paths = document.RootElement.GetProperty("paths");
        Assert.False(paths.TryGetProperty("/", out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARequest_ByDefault_LeavesItsQueryOutOfTheLog(bool productService)
    {
        var logged = await Logged(productService, configuration: null);

        Assert.DoesNotContain(logged, line => line.Contains(Credential, StringComparison.Ordinal));
    }

    // The control: the same request, with the category opened, writes the query; so the test above
    // is a test of the default and not of a request that logs nothing anyway.
    [Fact]
    public async Task ARequest_WhereTheServiceOpensTheCategory_IsLoggedWithItsQuery()
    {
        var logged = await Logged(
            productService: false,
            new Dictionary<string, string?> { [Service.RequestLineLogLevelKey] = "Information" });

        Assert.Contains(logged, line => line.Contains(Credential, StringComparison.Ordinal));
    }

    // A name is a lowercase slug; the human-readable form, passed in its place, is refused at once, naming the title
    // as where it belongs.
    [Theory]
    [InlineData("Widget Service")]
    [InlineData("widget_service")]
    [InlineData("-widgets")]
    [InlineData("widgets-")]
    [InlineData("widget--service")]
    public void Construct_ANameThatIsNotASlug_Throws(string name)
    {
        var exception = Assert.Throws<ArgumentException>(() => new Named(name, "Widget Service"));

        Assert.Equal("name", exception.ParamName);
        Assert.Contains("title", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "Widget Service", "name")]
    [InlineData(" ", "Widget Service", "name")]
    [InlineData("widget-service", "", "title")]
    public void Construct_ABlankNameOrTitle_Throws(string name, string title, string parameter) =>
        Assert.Equal(parameter, Assert.ThrowsAny<ArgumentException>(() => new Named(name, title)).ParamName);

    [Fact]
    public void Construct_ASlugAndATitle_AreKept()
    {
        var service = new Named("widget-service2", "Widget Service");

        Assert.Equal("widget-service2", service.GivenName);
        Assert.Equal("Widget Service", service.GivenTitle);
    }

    private static async Task<IReadOnlyCollection<string>> Logged(
        bool productService,
        IReadOnlyDictionary<string, string?>? configuration)
    {
        var lines = new LinesLogger();
        await using (var service = await SessionHost.StartAsync(
            productService,
            configuration,
            services => services.AddSingleton<ILoggerProvider>(lines)))
        {
            using var response = await service.Client().GetAsync(
                $"/docs/index.html?token={Credential}",
                TestContext.Current.CancellationToken);
        }

        return lines.Lines;
    }

    private sealed class Named(string name, string title) : Service(name, title, [])
    {
        public string GivenName => Name;

        public string GivenTitle => Title;
    }

    private sealed class LinesLogger : ILoggerProvider, ILogger
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public IReadOnlyCollection<string> Lines => _lines;

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _lines.Enqueue(formatter(state, exception));

        public void Dispose()
        {
        }
    }
}
