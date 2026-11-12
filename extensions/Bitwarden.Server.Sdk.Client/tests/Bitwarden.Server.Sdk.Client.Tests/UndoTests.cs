using Bitwarden.Server.Sdk.Errors;
using Bitwarden.Server.Sdk.Idempotency;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// An operation's writes in other services are taken back newest first, best effort and on a deadline of their own,
/// and the operation's own failure stays the answer.
/// </summary>
public sealed class UndoTests
{
    private readonly ListLogger _logger = new();

    [Fact]
    public async Task RunAsync_TakesBackNewestFirst()
    {
        var undone = new List<string>();
        var undo = new Undo(_logger);
        undo.Add("first", _ => Record(undone, "first"));
        undo.Add("second", _ => Record(undone, "second"));
        undo.Add("third", _ => Record(undone, "third"));

        await undo.RunAsync();

        Assert.Equal(["third", "second", "first"], undone);
        Assert.False(undo.Any);
    }

    [Fact]
    public async Task RunAsync_OneUndoFailing_RunsTheRestAndLogsItByWhatItWas()
    {
        var undone = new List<string>();
        var undo = new Undo(_logger);
        undo.Add("first", _ => Record(undone, "first"));
        undo.Add("grant 7 on collection 9", _ => throw new InvalidOperationException("down"));
        undo.Add("third", _ => Record(undone, "third"));

        await undo.RunAsync();

        Assert.Equal(["third", "first"], undone);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.Equal("Could not take back grant 7 on collection 9 after the action it was part of failed.", entry.Message);
    }

    [Fact]
    public async Task RunAsync_ForgetsWhatItTookBack()
    {
        var count = 0;
        var undo = new Undo(_logger);
        undo.Add("one", _ => Task.FromResult(++count));

        await undo.RunAsync();
        await undo.RunAsync();

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task RunAsync_GivesEachUndoItsOwnDeadline_NeverTheRequestsToken()
    {
        using var request = new CancellationTokenSource();
        var tokens = new List<CancellationToken>();
        var undo = new Undo(_logger);
        undo.Add("one", token => { tokens.Add(token); return Task.CompletedTask; });
        undo.Add("two", token => { tokens.Add(token); return Task.CompletedTask; });

        await request.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            undo.GuardAsync(() => Task.FromCanceled(request.Token)));

        Assert.Equal(2, tokens.Count);
        Assert.All(tokens, token => Assert.True(token.CanBeCanceled));
        Assert.All(tokens, token => Assert.False(token.IsCancellationRequested));
        Assert.NotEqual(tokens[0], tokens[1]);
    }

    [Fact]
    public async Task GuardAsync_StepFails_TakesBackAndThrowsTheStepsOwnFailure()
    {
        var undone = new List<string>();
        var undo = new Undo(_logger);
        undo.Add("first", _ => Record(undone, "first"));
        undo.Add("second", _ => throw new TimeoutException());
        var failure = new InvalidOperationException("the step");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => undo.GuardAsync(() => throw failure));

        Assert.Same(failure, thrown);
        Assert.Equal(["first"], undone);
    }

    [Fact]
    public async Task GuardAsync_StepSucceeds_KeepsTheWritesAndAnswersTheStep()
    {
        var undone = new List<string>();
        var undo = new Undo(_logger);
        undo.Add("first", _ => Record(undone, "first"));

        var answered = await undo.GuardAsync(() => Task.FromResult(42));

        Assert.Equal(42, answered);
        Assert.Empty(undone);
        Assert.True(undo.Any);
    }

    [Fact]
    public async Task GuardAsync_Cancelled_StillTakesBack()
    {
        var undone = new List<string>();
        var undo = new Undo(_logger);
        undo.Add("first", _ => Record(undone, "first"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            undo.GuardAsync(() => throw new OperationCanceledException()));

        Assert.Equal(["first"], undone);
    }

    public static TheoryData<Exception> Refusals() =>
    [
        new ConflictException(),
        new NotFoundException(),
        new ForbiddenException(),
        new InvalidRequestException("no"),
        new UnauthenticatedException(),
        new RequestRefusedException([]),
    ];

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task GuardAsync_OnRefusal_Refused_TakesBack(Exception refusal)
    {
        var undone = new List<string>();
        var undo = new Undo(_logger);
        undo.Add("first", _ => Record(undone, "first"));

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => undo.GuardAsync(() => throw refusal, UndoOn.Refusal));

        Assert.Same(refusal, thrown);
        Assert.Equal(["first"], undone);
    }

    public static TheoryData<Exception> MayHaveLanded() =>
    [
        new ServiceUnavailableException("widget-service", "did not answer"),
        new TimeoutException(),
        new OperationCanceledException(),
        new TaskCanceledException(),
        new HttpRequestException("reset"),
        new InvalidOperationException("500"),
    ];

    [Theory]
    [MemberData(nameof(MayHaveLanded))]
    public async Task GuardAsync_OnRefusal_MayHaveLanded_KeepsTheWrites(Exception failure)
    {
        var undone = new List<string>();
        var undo = new Undo(_logger);
        undo.Add("first", _ => Record(undone, "first"));

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => undo.GuardAsync(() => Task.FromException<int>(failure), UndoOn.Refusal));

        Assert.Same(failure, thrown);
        Assert.Empty(undone);
        Assert.True(undo.Any);
    }

    [Fact]
    public async Task Undo_InsideAnIdempotentOperation_RunsInsideIt()
    {
        string? seen = null;
        var undo = new Undo(_logger);
        undo.Add("first", _ => { seen = IdempotentOperation.Current; return Task.CompletedTask; });

        using (IdempotentOperation.Begin("remove-member:1"))
        {
            await Assert.ThrowsAsync<TimeoutException>(() => undo.GuardAsync(() => throw new TimeoutException()));
        }

        Assert.Equal("remove-member:1", seen);
    }

    [Fact]
    public void Add_BlankWrite_Throws()
    {
        var undo = new Undo(_logger);

        Assert.Throws<ArgumentException>(() => undo.Add(" ", _ => Task.CompletedTask));
    }

    [Fact]
    public void StartDeadline_CancelsAfterTheDeadline()
    {
        using var deadline = Undo.StartDeadline();

        Assert.Equal(TimeSpan.FromSeconds(30), Undo.Deadline);
        Assert.True(deadline.Token.CanBeCanceled);
        Assert.False(deadline.IsCancellationRequested);
    }

    private static Task Record(List<string> undone, string write)
    {
        undone.Add(write);
        return Task.CompletedTask;
    }

    private sealed record Entry(LogLevel Level, string Message, Exception? Exception);

    private sealed class ListLogger : ILogger
    {
        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(new Entry(logLevel, formatter(state, exception), exception));
    }
}
