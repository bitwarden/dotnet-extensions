using Bitwarden.Server.Sdk.Errors;
using Bitwarden.Server.Sdk.Idempotency;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// The writes one operation has made in other services so far, each with the write that takes it back, for an
/// operation that fails part-way: one transaction, rolled back, made of steps instead.
/// </summary>
/// <remarks>
/// <para>
/// An operation that writes to more than one service can't commit once. Each write it makes elsewhere is recorded
/// here, once it has succeeded, with what takes it back (<see cref="Add"/>); the steps after it run inside
/// <see cref="GuardAsync(Func{Task}, UndoOn)"/>, which takes back everything recorded when the step fails and then
/// answers with the step's own failure. Work that can follow the operation's commit (a message sent once it commits,
/// a retry) is better completed afterwards than taken back.
/// </para>
/// <para>
/// Undos run newest first. Each is best effort and never runs on the request's token: a request cancelled or timed
/// out after a write still takes the write back. Each runs on a deadline of its own (<see cref="Deadline"/>) instead.
/// An undo that fails is logged by what it was, identifiers only, and the rest still run; the operation's own failure
/// is still what the caller is answered.
/// </para>
/// <para>
/// A write that may have landed is never taken back on its own account: only a refusal (<see cref="IsRefusal"/>)
/// shows the callee wrote nothing. Guard such a step with <see cref="UndoOn.Refusal"/>.
/// </para>
/// <para>
/// One exception: an undo that finds out for itself whether its write landed, and takes back only what it finds
/// (it reads the state first and leaves it alone unless the write is there, or it writes back the whole value held
/// before, a no-op when nothing landed), may be recorded before its write, inside the guard. A write that lands with
/// its answer lost is then taken back too, where keeping it would leave access or seats nobody pays for.
/// </para>
/// <para>
/// An undo's writes go through the same clients as any other, so inside an <see cref="IdempotentOperation"/> they carry
/// keys derived from it: an operation run again from the start that fails the same way sends the same undo with the
/// same keys, and the callee does it at most once. Record what an undo is about inside the undo itself
/// (<see cref="OutgoingCall"/>), as for any call.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var undo = new Undo(logger);
///
/// var grant = await widgets.GrantAsync(widget, terms, cancellationToken);
/// undo.Add($"grant {grant.Id} on widget {widget}", token => widgets.RevokeAsync(widget, grant.Id, token));
///
/// // Refused, the grant is taken back and the refusal answered; failed otherwise, the delete may have landed and the
/// // grant stays.
/// await undo.GuardAsync(() => gadgets.DeleteAsync(gadget, cancellationToken), UndoOn.Refusal);
/// </code>
/// </example>
/// <param name="logger">Records an undo that failed, by identifiers only.</param>
public sealed class Undo(ILogger logger)
{
    private readonly Stack<(string Write, Func<CancellationToken, Task> TakeBack)> _steps = new();

    /// <summary>
    /// How long one undo may take before it is given up and logged: 30 seconds.
    /// </summary>
    public static TimeSpan Deadline { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether there is anything to take back.
    /// </summary>
    public bool Any => _steps.Count > 0;

    /// <summary>
    /// A token that cancels once <see cref="Deadline"/> has passed, for an undo written by hand rather than recorded
    /// here: it runs on this, never on the request's token.
    /// </summary>
    /// <returns>The deadline's source; dispose it once the undo is done.</returns>
    public static CancellationTokenSource StartDeadline() => new(Deadline);

    /// <summary>
    /// Whether a failure is a refusal: the service answered and wrote nothing. Any other failure (a timeout, an
    /// unreachable service, a <c>5xx</c>, the request cancelled) may follow a write that landed, so what was written
    /// before it is not taken back on its account: a commit that landed is never undone.
    /// </summary>
    /// <remarks>
    /// A <see cref="ConflictException"/> is the callee refusing (a stale version, a duplicate). A keyed write the callee
    /// is still running when the call ends (a <c>409</c> with <c>Retry-After</c> after the last retry) is not one: the
    /// client answers it as a <see cref="ServiceUnavailableException"/>, like a timeout, because the first attempt may
    /// still land.
    /// </remarks>
    /// <param name="failure">The step's failure.</param>
    /// <returns>True for a refusal.</returns>
    public static bool IsRefusal(Exception failure) =>
        failure is IProvideErrors
            or ConflictException
            or NotFoundException
            or ForbiddenException
            or InvalidRequestException
            or UnauthenticatedException;

    /// <summary>
    /// Records a write made, with what takes it back.
    /// </summary>
    /// <param name="write">The write, for the log: identifiers only, never a value.</param>
    /// <param name="takeBack">Takes the write back, on the token it is given (<see cref="Deadline"/>).</param>
    public void Add(string write, Func<CancellationToken, Task> takeBack)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(write);
        ArgumentNullException.ThrowIfNull(takeBack);

        _steps.Push((write, takeBack));
    }

    /// <summary>
    /// Runs <paramref name="step"/>; should it fail as <paramref name="on"/> says, takes back every write recorded and
    /// throws the step's own failure.
    /// </summary>
    /// <param name="step">The next step of the operation.</param>
    /// <param name="on">Which of its failures take the writes back.</param>
    /// <returns>Completes when the step has.</returns>
    public async Task GuardAsync(Func<Task> step, UndoOn on = UndoOn.AnyFailure)
    {
        ArgumentNullException.ThrowIfNull(step);

        try
        {
            await step();
        }
        catch (Exception failure) when (Undoes(failure, on))
        {
            await RunAsync();
            throw;
        }
    }

    /// <summary>
    /// Runs <paramref name="step"/> and answers what it answers; should it fail as <paramref name="on"/> says, takes back
    /// every write recorded and throws the step's own failure.
    /// </summary>
    /// <typeparam name="T">What the step answers.</typeparam>
    /// <param name="step">The next step of the operation.</param>
    /// <param name="on">Which of its failures take the writes back.</param>
    /// <returns>What the step answered.</returns>
    public async Task<T> GuardAsync<T>(Func<Task<T>> step, UndoOn on = UndoOn.AnyFailure)
    {
        ArgumentNullException.ThrowIfNull(step);

        try
        {
            return await step();
        }
        catch (Exception failure) when (Undoes(failure, on))
        {
            await RunAsync();
            throw;
        }
    }

    /// <summary>
    /// Takes back every write recorded, newest first, and forgets them. Never throws: an undo that fails is logged and
    /// the rest still run.
    /// </summary>
    /// <returns>Completes when every undo has run or failed.</returns>
    public async Task RunAsync()
    {
        while (_steps.TryPop(out var undo))
        {
            using var deadline = StartDeadline();

            try
            {
                await undo.TakeBack(deadline.Token);
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "Could not take back {Write} after the action it was part of failed.", undo.Write);
            }
        }
    }

    private static bool Undoes(Exception failure, UndoOn on) =>
        on == UndoOn.AnyFailure || IsRefusal(failure);
}
