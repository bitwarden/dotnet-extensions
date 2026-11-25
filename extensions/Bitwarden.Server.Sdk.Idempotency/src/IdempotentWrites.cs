using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Authorization;
using Bitwarden.Server.Sdk.Context;
using Bitwarden.Server.Sdk.Errors;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Idempotency;

/// <summary>
/// Does a write another service sends with an <c>Idempotency-Key</c> at most once, replaying its first completed
/// answer to a repeat (IETF draft "The Idempotency-Key HTTP Header Field").
/// </summary>
/// <remarks>
/// <para>
/// Applies to a write (any method but <c>GET</c>, <c>HEAD</c>, <c>OPTIONS</c> and <c>TRACE</c>) to a mapped operation
/// declaring <see cref="AcceptsIdempotencyKeyAttribute"/> and not asked only to be checked (<c>validateOnly=true</c>),
/// from another service's credential (<see cref="InternalCaller"/>), carrying the header. Everything else passes
/// untouched: an operation that doesn't declare it, and a browser's write, and so every client-facing operation, run
/// every time they arrive, whatever headers they carry.
/// </para>
/// <para>
/// A record is kept per caller (its <c>client_id</c>), key, and the owners its context names. The first request with a key holds it while it runs
/// (<see cref="IdempotencyOptions.Lease"/>); its answer, if it completed (any status below <c>500</c>, written by the
/// operation rather than thrown), is kept for <see cref="IdempotencyOptions.Retention"/>. A repeat with the same
/// method, path, query and body is answered with that answer, marked <c>Idempotency-Replayed: true</c>; one with a
/// different request is refused <c>422</c>; one while the first runs, <c>409</c> with <c>Retry-After</c>, which a
/// service client waits for within its deadline. A record this instance can't open is taken as running. A write that failed (a <c>5xx</c>,
/// or an exception, which the platform answers outside this) keeps nothing, so its repeat runs again.
/// </para>
/// <para>
/// What is kept is what the operation already answered the caller: Protected Data or metadata, never plaintext Vault
/// Data, which no service ever holds. It is sealed with the service's data protection keys before it is stored, since
/// the services that call this one may read its prefix of a shared Redis.
/// </para>
/// </remarks>
internal sealed class IdempotentWrites(
    RequestDelegate next,
    IIdempotencyStore store,
    IDataProtectionProvider protection,
    IServiceIdentity identity,
    IOptionsMonitor<IdempotencyOptions> options,
    ILogger<IdempotentWrites> logger)
{
    /// <summary>
    /// The headers a replay carries beside the body, as the first answer had them.
    /// </summary>
    private static readonly string[] KeptHeaders = ["Content-Type", "Location", "ETag", "Last-Modified"];

    // Not this package's name, and it stays as it is: a record sealed under one purpose can't be opened under another,
    // so changing it would turn every kept answer into a miss.
    private readonly IDataProtector _sealer = protection.CreateProtector("Bitwarden.Server.Sdk.Service.Idempotency.v1");

    public async Task InvokeAsync(HttpContext http)
    {
        var settings = options.CurrentValue;

        if (!settings.Enabled
            || !IsWrite(http.Request.Method)
            || !http.Request.Headers.TryGetValue(IdempotencyKeys.Header, out var values)
            || http.GetEndpoint() is not { } endpoint
            || endpoint.Metadata.GetMetadata<AcceptsIdempotencyKeyAttribute>() is null
            || IsValidateOnly(http.Request)
            || !InternalCaller.Is(http.User))
        {
            await next(http);
            return;
        }

        if (values.Count != 1 || !IdempotencyKeys.IsValid(values[0]))
        {
            throw new BadHttpRequestException(
                $"{IdempotencyKeys.Header} must be one value of 1 to {IdempotencyKeys.MaximumLength} visible ASCII characters.",
                StatusCodes.Status400BadRequest);
        }

        if (await FingerprintAsync(http.Request, settings.MaximumBytes) is not { } fingerprint)
        {
            await next(http);
            return;
        }

        var caller = http.User.FindFirst(InternalCaller.ClientClaim)!.Value;
        var key = StorageKey(identity.Name, caller, values[0]!, Owners(http));
        var pending = Seal(new Record { Fingerprint = fingerprint, Nonce = Guid.NewGuid().ToString("N") });
        var claim = await store.ClaimAsync(key, pending, settings.Lease);

        if (claim.Outcome == ClaimOutcome.Unavailable)
        {
            await next(http);
            return;
        }

        if (claim.Outcome == ClaimOutcome.Found)
        {
            // A record this instance can't open (a data protection key it lacks) is taken as held, never run past:
            // running it could do the write twice.
            if (Open(claim.Existing!) is not { } found)
            {
                logger.LogWarning(
                    "A write's idempotency record could not be opened (a data protection key this instance lacks); it is refused as still running.");
                await InProgressAsync(http);
                return;
            }

            if (found.Fingerprint != fingerprint)
            {
                throw new InvalidRequestException(
                    $"This {IdempotencyKeys.Header} was used for a different request. A key names one request; send a new key for a new one.");
            }

            if (found.Status is null)
            {
                await InProgressAsync(http);
                return;
            }

            await ReplayAsync(http, found);
            return;
        }

        await RunAndRecordAsync(http, key, pending, fingerprint, settings);
    }

    /// <summary>
    /// The prefix a service's records are kept under: <c>{service}:idempotency:</c>.
    /// </summary>
    /// <remarks>
    /// It starts with <c>{service}:</c>, the prefix a Redis user of the service's own is granted to write, so a store
    /// shared with other services' keys still holds the records under the service's own name.
    /// </remarks>
    /// <param name="service">The service's name. It may not contain <c>:</c>, <c>{</c> or <c>}</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="service"/> is empty, or contains <c>:</c>, <c>{</c> or <c>}</c>.</exception>
    internal static string Prefix(string service)
    {
        // A ':' would let two services' records share a prefix, and a brace would read as a key template's placeholder.
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        if (service.AsSpan().IndexOfAny(':', '{', '}') >= 0)
        {
            throw new ArgumentException("A name in a key may not contain ':', '{' or '}'.", nameof(service));
        }

        return $"{service}:idempotency:";
    }

    /// <summary>
    /// The storage key: the service's own prefix, and a digest of the caller, its key and whom the call is about.
    /// </summary>
    /// <remarks>
    /// Whom the call is about (<paramref name="owners"/>) is part of the key, so a caller that sends one key for two
    /// people's requests runs both, and never answers one person with the other's answer. A call about nobody keeps
    /// the digest of the caller and its key alone.
    /// </remarks>
    internal static string StorageKey(string service, string caller, string idempotencyKey, string? owners = null) =>
        Prefix(service)
        + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.IsNullOrEmpty(owners) ? $"{caller}\n{idempotencyKey}" : $"{caller}\n{idempotencyKey}\n{owners}")));

    /// <summary>
    /// The organization, user and provider the call's verified context names, or null when it names none.
    /// </summary>
    internal static string? Owners(HttpContext http) =>
        http.RequestServices?.GetService(typeof(IContext)) is IContext { } context
        && (context.OrganizationId is not null || context.UserId is not null || context.ProviderId is not null)
            ? $"{context.OrganizationId}\n{context.UserId}\n{context.ProviderId}"
            : null;

    private async Task RunAndRecordAsync(HttpContext http, string key, string pending, string fingerprint, IdempotencyOptions settings)
    {
        var original = http.Response.Body;
        using var capture = new CapturingStream(original, settings.MaximumBytes);
        http.Response.Body = capture;

        try
        {
            await next(http);
        }
        catch
        {
            await store.ReleaseAsync(key, pending);
            throw;
        }
        finally
        {
            http.Response.Body = original;
        }

        if (http.Response.StatusCode >= StatusCodes.Status500InternalServerError || capture.Overflowed)
        {
            await store.ReleaseAsync(key, pending);
            return;
        }

        var answer = new Record
        {
            Fingerprint = fingerprint,
            Status = http.Response.StatusCode,
            Headers = KeptHeaders
                .Where(name => http.Response.Headers.ContainsKey(name))
                .ToDictionary(name => name, name => http.Response.Headers[name].ToString(), StringComparer.OrdinalIgnoreCase),
            Body = capture.Captured,
        };

        await store.CompleteAsync(key, Seal(answer), settings.Retention);
    }

    /// <summary>
    /// <c>409</c>, with <c>Retry-After</c>: the first request with this key is still running. Written here rather than
    /// thrown, since the exception handler would clear the header; a service client waits and tries again within its
    /// deadline, and gets the replayed answer once the first has finished.
    /// </summary>
    private static Task InProgressAsync(HttpContext http)
    {
        http.Response.StatusCode = StatusCodes.Status409Conflict;
        http.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        http.Response.ContentType = System.Net.Mime.MediaTypeNames.Application.Json;

        return http.Response.WriteAsJsonAsync(
            new Bitwarden.Server.Sdk.Errors.ErrorResponse
            {
                Errors =
                [
                    new Bitwarden.Server.Sdk.Errors.Error
                    {
                        Status = "409",
                        Code = Bitwarden.Server.Sdk.Errors.ErrorCodes.Conflict,
                        Title = Bitwarden.Server.Sdk.Errors.ErrorTitles.Conflict,
                        Detail = $"A request with this {IdempotencyKeys.Header} is still being processed. Try again once it has finished.",
                    },
                ],
            });
    }

    /// <summary>
    /// The <c>Retry-After</c> of a refusal while the first request runs.
    /// </summary>
    internal const int RetryAfterSeconds = 1;

    private static async Task ReplayAsync(HttpContext http, Record answer)
    {
        http.Response.StatusCode = answer.Status!.Value;

        foreach (var (name, value) in answer.Headers ?? [])
        {
            http.Response.Headers[name] = value;
        }

        http.Response.Headers[IdempotencyKeys.ReplayedHeader] = "true";

        if (answer.Body is { Length: > 0 } body)
        {
            http.Response.ContentLength = body.Length;
            await http.Response.Body.WriteAsync(body);
        }
    }

    /// <summary>
    /// A digest of the method, the path and query, and the body; or null when the body is past
    /// <paramref name="limit"/>. Leaves the body where the operation will read it from.
    /// </summary>
    internal static async Task<string?> FingerprintAsync(HttpRequest request, int limit)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        digest.AppendData(Encoding.UTF8.GetBytes($"{request.Method}\n{request.Path}{request.QueryString}\n"));

        request.EnableBuffering();
        var buffer = new byte[16 * 1024];
        long read = 0;

        try
        {
            int count;
            while ((count = await request.Body.ReadAsync(buffer)) > 0)
            {
                read += count;
                if (read > limit)
                {
                    return null;
                }

                digest.AppendData(buffer, 0, count);
            }
        }
        finally
        {
            request.Body.Position = 0;
        }

        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    private static bool IsWrite(string method) =>
        !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method) && !HttpMethods.IsTrace(method);

    /// <summary>
    /// Whether the request asks only to be checked (<c>validateOnly=true</c>): it changes nothing, so it is answered
    /// afresh every time rather than from a record, which could be older than the state it checks.
    /// </summary>
    internal static bool IsValidateOnly(HttpRequest request) =>
        request.Query.TryGetValue(ValidateOnlyParameter, out var values)
        && values.Count == 1
        && bool.TryParse(values[0], out var validateOnly)
        && validateOnly;

    /// <summary>
    /// The query parameter by which a write asks only to be checked.
    /// </summary>
    internal const string ValidateOnlyParameter = "validateOnly";

    private string Seal(Record record) =>
        _sealer.Protect(JsonSerializer.Serialize(record, IdempotencyJsonContext.Default.Record));

    private Record? Open(string sealedRecord)
    {
        try
        {
            return JsonSerializer.Deserialize(_sealer.Unprotect(sealedRecord), IdempotencyJsonContext.Default.Record);
        }
        catch (Exception failure) when (failure is CryptographicException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A key's record: the request's fingerprint, and once it completed, its answer.
    /// </summary>
    internal sealed record Record
    {
        [JsonPropertyName("f")]
        public required string Fingerprint { get; init; }

        /// <summary>
        /// Makes a pending record this request's own, so only it lets go of the key.
        /// </summary>
        [JsonPropertyName("n")]
        public string? Nonce { get; init; }

        [JsonPropertyName("s")]
        public int? Status { get; init; }

        [JsonPropertyName("h")]
        public Dictionary<string, string>? Headers { get; init; }

        [JsonPropertyName("b")]
        public byte[]? Body { get; init; }
    }
}
