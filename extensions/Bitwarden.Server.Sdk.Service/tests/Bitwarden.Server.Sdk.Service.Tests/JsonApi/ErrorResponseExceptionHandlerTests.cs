using System.Text.Json;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// How an exception is answered: a library's exception that means one of the platform's is answered
/// as that one, and a dependency that is down is answered as unavailable without disclosing how it
/// failed.
/// </summary>
public sealed class ErrorResponseExceptionHandlerTests
{
    [Fact]
    public async Task Handle_ATranslatedException_IsAnsweredAsWhatItMeans()
    {
        var (status, error) = await HandleTranslated(new LibraryRefusal(), new ToConflict());

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Equal("conflict", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Handle_AnExceptionNoTranslatorKnows_IsAFaultOfOurs()
    {
        var (status, _) = await HandleTranslated(new InvalidOperationException(), new ToConflict());

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
    }

    // The standards' 503, never the 500 that says this service has a bug.
    [Fact]
    public async Task Handle_DependencyUnavailable_ServiceUnavailableNamingIt()
    {
        var (status, error) = await Handle(ServiceUnavailableException.Unreachable(
            "organization-service", new HttpRequestException("Connection refused (organization.internal:443)")));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        Assert.Equal("503", error.GetProperty("status").GetString());
        Assert.Equal("service-unavailable", error.GetProperty("code").GetString());
        Assert.Equal("The organization-service service could not be reached.", error.GetProperty("detail").GetString());
    }

    // The caller learns which service, not how it is reached.
    [Fact]
    public async Task Handle_DependencyUnavailable_NothingOfTheFailureDisclosed()
    {
        var (_, error) = await Handle(ServiceUnavailableException.Unreachable(
            "organization-service", new HttpRequestException("Connection refused (organization.internal:443)")));

        Assert.DoesNotContain("organization.internal", error.GetRawText(), StringComparison.Ordinal);
    }

    // An operator correlates the report with the logged failure by this.
    [Fact]
    public async Task Handle_DependencyUnavailable_CarriesTheErrorId()
    {
        var (_, error) = await Handle(ServiceUnavailableException.Answered("billing-service", 503), traceIdentifier: "trace-1");

        Assert.Equal("trace-1", error.GetProperty("id").GetString());
    }

    // The server refuses a body over its size limit while the body is read, and says 413 itself.
    [Fact]
    public async Task Handle_BodyTooLarge_AnsweredWithTheServersStatus()
    {
        var (status, error) = await Handle(
            new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, status);
        Assert.Equal("413", error.GetProperty("status").GetString());
        Assert.Equal("invalid-request", error.GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, error.GetProperty("id").ValueKind);
    }

    // A client that stops sending part way through its body.
    [Fact]
    public async Task Handle_BodyCutOff_AnsweredWithTheServersStatus()
    {
        var (status, error) = await Handle(
            new BadHttpRequestException("Unexpected end of request content.", StatusCodes.Status400BadRequest));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal("400", error.GetProperty("status").GetString());
        Assert.Equal("invalid-request", error.GetProperty("code").GetString());
    }

    // A caller's mistake is not a fault of ours, and logging it as one would bury the faults.
    [Theory]
    [MemberData(nameof(CallerMistakes))]
    public async Task Handle_CallersMistake_LoggedAtDebug(Exception exception)
    {
        var logger = new RecordingLogger();

        await Handle(exception, logger: logger);

        Assert.Equal(LogLevel.Debug, Assert.Single(logger.Levels));
    }

    public static TheoryData<Exception> CallerMistakes() =>
    [
        new UnauthenticatedException(),
        new ForbiddenException(),
        new NotFoundException(),
        new ConflictException(),
        new InvalidRequestException("Not a permitted value."),
        new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge),
        new BadHttpRequestException("Unexpected end of request content.", StatusCodes.Status400BadRequest),
    ];

    [Theory]
    [MemberData(nameof(Faults))]
    public async Task Handle_FaultOfOurs_LoggedAtError(Exception exception)
    {
        var logger = new RecordingLogger();

        await Handle(exception, logger: logger);

        Assert.Equal(LogLevel.Error, Assert.Single(logger.Levels));
    }

    public static TheoryData<Exception> Faults() =>
    [
        new InvalidOperationException(),
        ServiceUnavailableException.Answered("billing-service", 503),
    ];

    private static async Task<(int Status, JsonElement Error)> Handle(
        Exception exception,
        string traceIdentifier = "trace",
        ILogger<ErrorResponseExceptionHandler>? logger = null)
    {
        var context = new DefaultHttpContext { TraceIdentifier = traceIdentifier };
        context.Response.Body = new MemoryStream();

        var handled = await new ErrorResponseExceptionHandler(logger ?? NullLogger<ErrorResponseExceptionHandler>.Instance, [])
            .TryHandleAsync(context, exception, TestContext.Current.CancellationToken);

        Assert.True(handled);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);

        return (context.Response.StatusCode, document.RootElement.GetProperty("errors")[0].Clone());
    }

    private static async Task<(int Status, JsonElement Error)> HandleTranslated(Exception exception, IExceptionTranslator translator)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await new ErrorResponseExceptionHandler(NullLogger<ErrorResponseExceptionHandler>.Instance, [translator])
            .TryHandleAsync(context, exception, TestContext.Current.CancellationToken);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);

        return (context.Response.StatusCode, document.RootElement.GetProperty("errors")[0].Clone());
    }

    private sealed class LibraryRefusal : Exception;

    private sealed class RecordingLogger : ILogger<ErrorResponseExceptionHandler>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }

    private sealed class ToConflict : IExceptionTranslator
    {
        public Exception? Translate(Exception exception) => exception is LibraryRefusal ? new ConflictException() : null;
    }
}
