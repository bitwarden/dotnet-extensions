using Bitwarden.Server.Sdk.Idempotency;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Bitwarden.Server.Sdk.Service.Tests.TestHost;

/// <summary>
/// Writes that count how often they ran, for idempotency keys. Left out of the OpenAPI document, so the recorded
/// documents of the parity suite are unchanged.
/// </summary>
public static class IdempotencyProbes
{
    /// <summary>
    /// Maps the probes.
    /// </summary>
    /// <param name="endpoints">The service's endpoints.</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var probes = endpoints.MapGroup("api/v1/idempotency-probes").ExcludeFromDescription();
        probes.MapPost("", Write);
        probes.MapPut("{id}", Replace);
        probes.MapPost("ignored", WriteIgnoringKeys);
        probes.MapPost("failing", Fail);
        probes.MapPost("throwing", Throw);
        probes.MapPost("slow", WriteSlowly);
    }

    /// <summary>
    /// Counts, and answers 201 with where and what it wrote.
    /// </summary>
    [AcceptsIdempotencyKey]
    public static Created<IdempotencyProbeAnswer> Write(IdempotencyProbeNote note, [FromServices] IdempotencyProbeCounter counter)
    {
        var count = counter.Increment();
        return TypedResults.Created($"/api/v1/idempotency-probes/{count}", new IdempotencyProbeAnswer(count, note.Note));
    }

    /// <summary>
    /// Counts, and answers 200.
    /// </summary>
    [AcceptsIdempotencyKey]
    public static Ok<IdempotencyProbeAnswer> Replace(string id, IdempotencyProbeNote note, [FromServices] IdempotencyProbeCounter counter) =>
        TypedResults.Ok(new IdempotencyProbeAnswer(counter.Increment(), $"{id}:{note.Note}"));

    /// <summary>
    /// Counts, whatever key it carries: it doesn't accept keys.
    /// </summary>
    public static Ok<IdempotencyProbeAnswer> WriteIgnoringKeys(IdempotencyProbeNote note, [FromServices] IdempotencyProbeCounter counter) =>
        TypedResults.Ok(new IdempotencyProbeAnswer(counter.Increment(), note.Note));

    /// <summary>
    /// Counts, and answers 500 itself.
    /// </summary>
    [AcceptsIdempotencyKey]
    public static StatusCodeHttpResult Fail([FromServices] IdempotencyProbeCounter counter)
    {
        counter.Increment();
        return TypedResults.StatusCode(StatusCodes.Status500InternalServerError);
    }

    /// <summary>
    /// Counts, and throws, which the platform answers 500.
    /// </summary>
    [AcceptsIdempotencyKey]
    public static Ok Throw([FromServices] IdempotencyProbeCounter counter)
    {
        counter.Increment();
        throw new InvalidOperationException("The probe failed.");
    }

    /// <summary>
    /// Counts, then waits for the test to let it finish.
    /// </summary>
    [AcceptsIdempotencyKey]
    public static async Task<Ok<IdempotencyProbeAnswer>> WriteSlowly(IdempotencyProbeNote note, [FromServices] IdempotencyProbeCounter counter)
    {
        var count = counter.Increment();
        await counter.Gate.Task;
        return TypedResults.Ok(new IdempotencyProbeAnswer(count, note.Note));
    }
}

/// <summary>
/// What a probe write sends.
/// </summary>
/// <param name="Note">Anything.</param>
public sealed record IdempotencyProbeNote(string? Note);

/// <summary>
/// What a probe write answers.
/// </summary>
/// <param name="Count">How many probe writes ran, this one included.</param>
/// <param name="Note">What it was sent.</param>
public sealed record IdempotencyProbeAnswer(int Count, string? Note);
