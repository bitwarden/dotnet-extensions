using System.Security.Claims;
using System.Text;
using Bitwarden.Server.Sdk.Authorization;
using Bitwarden.Server.Sdk.Context;
using Bitwarden.Server.Sdk.Errors;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecurityClaim = System.Security.Claims.Claim;

namespace Bitwarden.Server.Sdk.Idempotency.Tests;

/// <summary>
/// A write another service sends with an <c>Idempotency-Key</c> is done at most once: a repeat is answered with the
/// first answer, a repeat with another request is refused 422, one while the first runs 409. A browser's write, and a
/// write to an operation that doesn't accept keys, run every time.
/// </summary>
public sealed class IdempotentWritesTests
{
    private const string Service = "widget-service";

    private const string Caller = "internal.gadget-service";

    private const string Path = "/api/v1/widgets";

    private readonly IdempotencyOptions _settings = new();

    private readonly RecordingStore _store = new(new MemoryIdempotencyStore(TimeProvider.System));

    private readonly IDataProtectionProvider _keyRing = new EphemeralDataProtectionProvider();

    private readonly Operation _operation = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // A Redis user of the service's own is granted to write {service}:* and nothing else, so every record must sit
    // inside that prefix, or a service confined to its own prefix couldn't keep its records.
    [Fact]
    public void Prefix_StartsWithTheServicesOwnPrefix()
    {
        var prefix = IdempotentWrites.Prefix("widget-service");

        Assert.StartsWith("widget-service:", prefix, StringComparison.Ordinal);
        Assert.Equal("widget-service:idempotency:", prefix);
    }

    [Fact]
    public void StorageKey_StartsWithThePrefix()
    {
        var key = IdempotentWrites.StorageKey("widget-service", "internal.gadget-service", "k");

        Assert.StartsWith(IdempotentWrites.Prefix("widget-service"), key, StringComparison.Ordinal);
    }

    // The caller and its key are digested: neither shows in the store's keys, and two callers' keys never meet.
    [Fact]
    public void StorageKey_IsPerCallerAndHidesTheCaller()
    {
        var key = IdempotentWrites.StorageKey("widget-service", "internal.gadget-service", "k");

        Assert.DoesNotContain("internal.gadget-service", key, StringComparison.Ordinal);
        Assert.NotEqual(key, IdempotentWrites.StorageKey("widget-service", "internal.widget-service", "k"));
        Assert.NotEqual(key, IdempotentWrites.StorageKey("widget-service", "internal.gadget-service", "l"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("widget:service")]
    [InlineData("widget{service")]
    [InlineData("widget}service")]
    public void Prefix_RefusesANameThatIsNotOne(string service)
    {
        Assert.ThrowsAny<ArgumentException>(() => IdempotentWrites.Prefix(service));
    }

    [Fact]
    public void Prefix_RefusesNull()
    {
        Assert.Throws<ArgumentNullException>(() => IdempotentWrites.Prefix(null!));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public async Task ARead_RunsEveryTime(string method)
    {
        await SendAsync(Request(method: method));
        await SendAsync(Request(method: method));

        Assert.Equal(2, _operation.Runs);
        Assert.Equal(0, _store.Claims);
    }

    // A browser's write runs every time it arrives, whatever headers it carries.
    [Theory]
    [InlineData("web")]
    [InlineData("internal.")]
    [InlineData(null)]
    public async Task ABrowsersWrite_IgnoresTheKey(string? client)
    {
        await SendAsync(Request(client: client));
        var second = await SendAsync(Request(client: client, body: "{\"name\":\"other\"}"));

        Assert.Equal(StatusCodes.Status201Created, second.Response.StatusCode);
        Assert.False(second.Response.Headers.ContainsKey(IdempotencyKeys.ReplayedHeader));
        Assert.Equal(2, _operation.Runs);
        Assert.Equal(0, _store.Claims);
    }

    [Fact]
    public async Task AnUnauthenticatedCallerWithAnInternalClaim_IgnoresTheKey()
    {
        await SendAsync(Request(authenticated: false));
        await SendAsync(Request(authenticated: false));

        Assert.Equal(2, _operation.Runs);
        Assert.Equal(0, _store.Claims);
    }

    [Fact]
    public async Task AnOperationThatDoesNotAcceptKeys_RunsEveryTime()
    {
        await SendAsync(Request(endpoint: Endpoint()));
        var second = await SendAsync(Request(endpoint: Endpoint()));

        Assert.False(second.Response.Headers.ContainsKey(IdempotencyKeys.ReplayedHeader));
        Assert.Equal(2, _operation.Runs);
        Assert.Equal(0, _store.Claims);
    }

    // No operation was chosen: nothing to record against.
    [Fact]
    public async Task ARequestWithoutAnEndpoint_RunsEveryTime()
    {
        await SendAsync(Request(withEndpoint: false));
        await SendAsync(Request(withEndpoint: false));

        Assert.Equal(2, _operation.Runs);
        Assert.Equal(0, _store.Claims);
    }

    // A write asked only to be checked changes nothing: it is answered afresh each time, never from a record.
    [Theory]
    [InlineData("?validateOnly=true", 2)]
    [InlineData("?validateOnly=True", 2)]
    [InlineData("?validateOnly=false", 1)]
    [InlineData("?validateOnly=yes", 1)]
    [InlineData("?validateOnly=true&validateOnly=true", 1)]
    public async Task AWriteAskedOnlyToBeChecked_IsNeverRecorded(string query, int runs)
    {
        await SendAsync(Request(query: query));
        await SendAsync(Request(query: query));

        Assert.Equal(runs, _operation.Runs);
    }

    [Fact]
    public async Task AWriteWithoutAKey_RunsEveryTime()
    {
        await SendAsync(Request(key: null));
        await SendAsync(Request(key: null));

        Assert.Equal(2, _operation.Runs);
        Assert.Equal(0, _store.Claims);
    }

    [Fact]
    public async Task TurnedOff_KeysAreIgnored()
    {
        _settings.Enabled = false;

        await SendAsync(Request());
        await SendAsync(Request());

        Assert.Equal(2, _operation.Runs);
        Assert.Equal(0, _store.Claims);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has a space")]
    [InlineData("tab\tinside")]
    [InlineData("café")]
    public async Task AMalformedKey_IsRefused400(string key)
    {
        var refused = await Assert.ThrowsAsync<BadHttpRequestException>(() => SendAsync(Request(key: key)));

        Assert.Equal(StatusCodes.Status400BadRequest, refused.StatusCode);
        Assert.Equal(0, _operation.Runs);
    }

    [Fact]
    public async Task AKeyTooLong_IsRefused400()
    {
        var refused = await Assert.ThrowsAsync<BadHttpRequestException>(
            () => SendAsync(Request(key: new string('k', IdempotencyKeys.MaximumLength + 1))));

        Assert.Equal(StatusCodes.Status400BadRequest, refused.StatusCode);
        Assert.Equal(0, _operation.Runs);
    }

    [Fact]
    public async Task AKeySentTwice_IsRefused400()
    {
        var http = Request();
        http.Request.Headers[IdempotencyKeys.Header] = new(["key-1", "key-1"]);

        var refused = await Assert.ThrowsAsync<BadHttpRequestException>(() => SendAsync(http));

        Assert.Equal(StatusCodes.Status400BadRequest, refused.StatusCode);
        Assert.Equal(0, _operation.Runs);
    }

    [Fact]
    public async Task TheFirstWrite_RunsAndIsRecorded()
    {
        _settings.Lease = TimeSpan.FromMinutes(5);
        _settings.Retention = TimeSpan.FromHours(3);

        var first = await SendAsync(Request());

        Assert.Equal(StatusCodes.Status201Created, first.Response.StatusCode);
        Assert.Equal(Operation.Answer, Body(first));
        Assert.False(first.Response.Headers.ContainsKey(IdempotencyKeys.ReplayedHeader));
        Assert.Equal(1, _operation.Runs);
        Assert.Equal(1, _store.Completes);
        Assert.Equal(0, _store.Releases);
        Assert.Equal(TimeSpan.FromMinutes(5), _store.LastLease);
        Assert.Equal(TimeSpan.FromHours(3), _store.LastRetention);
        Assert.Equal(IdempotentWrites.StorageKey(Service, Caller, "key-1"), _store.LastKey);
    }

    // The fingerprint reads the body first; the operation still reads all of it.
    [Fact]
    public async Task TheOperation_StillReadsTheBody()
    {
        await SendAsync(Request(body: "{\"name\":\"first widget\"}"));

        Assert.Equal("{\"name\":\"first widget\"}", _operation.LastBody);
    }

    // What is kept is sealed with the service's keys before it reaches the store, which other services may read.
    [Fact]
    public async Task WhatIsKept_IsSealed()
    {
        await SendAsync(Request());

        Assert.NotNull(_store.LastAnswer);
        Assert.DoesNotContain("widget-1", _store.LastAnswer, StringComparison.Ordinal);
        Assert.DoesNotContain(Path, _store.LastAnswer, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task ARepeat_IsAnsweredAsTheFirstWasWithoutRunningAgain(string method)
    {
        var first = await SendAsync(Request(method: method));
        var repeat = await SendAsync(Request(method: method));

        Assert.Equal(StatusCodes.Status201Created, repeat.Response.StatusCode);
        Assert.Equal(Body(first), Body(repeat));
        Assert.Equal(Encoding.UTF8.GetByteCount(Operation.Answer), repeat.Response.ContentLength);
        Assert.Equal("application/json; charset=utf-8", repeat.Response.ContentType);
        Assert.Equal("/api/v1/widgets/widget-1", repeat.Response.Headers.Location.ToString());
        Assert.Equal("\"v1\"", repeat.Response.Headers.ETag.ToString());
        Assert.Equal("Tue, 10 Nov 2026 12:00:00 GMT", repeat.Response.Headers.LastModified.ToString());
        Assert.Equal("true", repeat.Response.Headers[IdempotencyKeys.ReplayedHeader].ToString());
        Assert.Equal(1, _operation.Runs);
    }

    // Only the headers that describe the answer are kept, not whatever else the operation set.
    [Fact]
    public async Task ARepeat_CarriesOnlyTheKeptHeaders()
    {
        var first = await SendAsync(Request());
        var repeat = await SendAsync(Request());

        Assert.True(first.Response.Headers.ContainsKey("X-Widget-Trace"));
        Assert.False(repeat.Response.Headers.ContainsKey("X-Widget-Trace"));
    }

    [Fact]
    public async Task AnAnswerWithoutABody_IsReplayedWithoutOne()
    {
        _operation.Respond = http =>
        {
            http.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        };

        await SendAsync(Request(method: "DELETE"));
        var repeat = await SendAsync(Request(method: "DELETE"));

        Assert.Equal(StatusCodes.Status204NoContent, repeat.Response.StatusCode);
        Assert.Equal(string.Empty, Body(repeat));
        Assert.Equal("true", repeat.Response.Headers[IdempotencyKeys.ReplayedHeader].ToString());
        Assert.Equal(1, _operation.Runs);
    }

    // A refusal the operation answered (below 500) is an answer too: its repeat gets the same refusal.
    [Fact]
    public async Task ARefusalTheOperationAnswered_IsReplayed()
    {
        _operation.Respond = http =>
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        };

        await SendAsync(Request());
        var repeat = await SendAsync(Request());

        Assert.Equal(StatusCodes.Status404NotFound, repeat.Response.StatusCode);
        Assert.Equal(1, _operation.Runs);
    }

    [Fact]
    public async Task TwoKeys_RunTwice()
    {
        await SendAsync(Request(key: "key-1"));
        await SendAsync(Request(key: "key-2"));

        Assert.Equal(2, _operation.Runs);
    }

    // A key names one request: another method, path, query or body under it is refused, and the first stands.
    [Theory]
    [InlineData("POST", Path, "", "{\"name\":\"other\"}")]
    [InlineData("PUT", Path, "", Operation.Request)]
    [InlineData("POST", Path + "/widget-1", "", Operation.Request)]
    [InlineData("POST", Path, "?extra=1", Operation.Request)]
    public async Task AKeyReusedForAnotherRequest_IsRefused422(string method, string path, string query, string body)
    {
        await SendAsync(Request());

        var refused = await Assert.ThrowsAsync<InvalidRequestException>(
            () => SendAsync(Request(method: method, path: path, query: query, body: body)));

        Assert.Contains(IdempotencyKeys.Header, refused.Message, StringComparison.Ordinal);
        Assert.Equal(1, _operation.Runs);

        var repeat = await SendAsync(Request());
        Assert.Equal("true", repeat.Response.Headers[IdempotencyKeys.ReplayedHeader].ToString());
    }

    [Fact]
    public async Task ARepeatWhileTheFirstRuns_IsRefused409_ThenReplayed()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var respond = _operation.Respond;
        _operation.Respond = async http =>
        {
            await gate.Task;
            await respond(http);
        };

        var running = SendAsync(Request());
        var meanwhile = await SendAsync(Request());

        Assert.Equal(StatusCodes.Status409Conflict, meanwhile.Response.StatusCode);
        Assert.Equal("1", meanwhile.Response.Headers.RetryAfter.ToString());
        Assert.Contains("\"code\":\"conflict\"", Body(meanwhile), StringComparison.Ordinal);
        Assert.False(meanwhile.Response.Headers.ContainsKey(IdempotencyKeys.ReplayedHeader));

        gate.SetResult();
        var first = await running;
        var after = await SendAsync(Request());

        Assert.Equal(StatusCodes.Status201Created, after.Response.StatusCode);
        Assert.Equal(Body(first), Body(after));
        Assert.Equal(1, _operation.Runs);
    }

    // A write that failed keeps nothing: its repeat runs again.
    [Theory]
    [InlineData(StatusCodes.Status500InternalServerError)]
    [InlineData(StatusCodes.Status503ServiceUnavailable)]
    public async Task AFailedWrite_RunsAgainOnItsRepeat(int status)
    {
        _operation.Respond = http =>
        {
            http.Response.StatusCode = status;
            return Task.CompletedTask;
        };

        var first = await SendAsync(Request());
        var repeat = await SendAsync(Request());

        Assert.Equal(status, first.Response.StatusCode);
        Assert.Equal(status, repeat.Response.StatusCode);
        Assert.False(repeat.Response.Headers.ContainsKey(IdempotencyKeys.ReplayedHeader));
        Assert.Equal(2, _operation.Runs);
        Assert.Equal(0, _store.Completes);
        Assert.Equal(2, _store.Releases);
    }

    [Fact]
    public async Task AWriteThatThrew_RunsAgainOnItsRepeat()
    {
        _operation.Respond = _ => throw new InvalidOperationException("The widget store failed.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(Request()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(Request()));

        Assert.Equal(2, _operation.Runs);
        Assert.Equal(0, _store.Completes);
        Assert.Equal(2, _store.Releases);
    }

    // The response stream is put back however the operation ends, so whatever answers an exception writes reaches
    // the caller.
    [Fact]
    public async Task AWriteThatThrew_LeavesTheResponseStreamAsItWas()
    {
        _operation.Respond = _ => throw new InvalidOperationException("The widget store failed.");
        var http = Request();
        var original = http.Response.Body;

        await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(http));

        Assert.Same(original, http.Response.Body);
    }

    // An answer too large to keep still reaches the caller in full; only its record is let go.
    [Fact]
    public async Task AnAnswerPastTheLimit_IsNotKept()
    {
        _settings.MaximumBytes = Encoding.UTF8.GetByteCount(Operation.Answer) - 1;

        var first = await SendAsync(Request());
        var repeat = await SendAsync(Request());

        Assert.Equal(Operation.Answer, Body(first));
        Assert.Equal(Operation.Answer, Body(repeat));
        Assert.False(repeat.Response.Headers.ContainsKey(IdempotencyKeys.ReplayedHeader));
        Assert.Equal(2, _operation.Runs);
        Assert.Equal(0, _store.Completes);
    }

    [Fact]
    public async Task AnAnswerAtTheLimit_IsKept()
    {
        _settings.MaximumBytes = Encoding.UTF8.GetByteCount(Operation.Answer);

        await SendAsync(Request());
        var repeat = await SendAsync(Request());

        Assert.Equal(Operation.Answer, Body(repeat));
        Assert.Equal(1, _operation.Runs);
    }

    // A body past the limit is run without a key rather than fingerprinted, and the operation still reads all of it.
    [Fact]
    public async Task ABodyPastTheLimit_RunsWithoutAKey()
    {
        _settings.MaximumBytes = 10;
        const string body = "{\"name\":\"a widget name longer than ten bytes\"}";

        await SendAsync(Request(body: body));
        await SendAsync(Request(body: body));

        Assert.Equal(2, _operation.Runs);
        Assert.Equal(0, _store.Claims);
        Assert.Equal(body, _operation.LastBody);
    }

    // A store that can't answer never stops a write: it runs, as it would without a key.
    [Fact]
    public async Task AStoreThatCannotAnswer_LetsTheWriteRun()
    {
        var unavailable = new Unavailable();

        await SendAsync(Request(), store: unavailable);
        var second = await SendAsync(Request(), store: unavailable);

        Assert.Equal(StatusCodes.Status201Created, second.Response.StatusCode);
        Assert.False(second.Response.Headers.ContainsKey(IdempotencyKeys.ReplayedHeader));
        Assert.Equal(2, _operation.Runs);
        Assert.Equal(0, unavailable.Completes);
    }

    // A record this instance can't open (another key ring) is taken as running, never run past: running it could do
    // the write twice.
    [Fact]
    public async Task ARecordSealedUnderAnotherKeyRing_IsRefusedAsRunning()
    {
        await SendAsync(Request());

        var refused = await SendAsync(Request(), keyRing: new EphemeralDataProtectionProvider());

        Assert.Equal(StatusCodes.Status409Conflict, refused.Response.StatusCode);
        Assert.Equal("1", refused.Response.Headers.RetryAfter.ToString());
        Assert.Equal(1, _operation.Runs);
    }

    [Fact]
    public async Task ARecordThatIsNotOne_IsRefusedAsRunning()
    {
        var refused = await SendAsync(Request(), store: new Holding("not a sealed record"));

        Assert.Equal(StatusCodes.Status409Conflict, refused.Response.StatusCode);
        Assert.Equal(0, _operation.Runs);
    }

    // A record that unseals under this instance's keys but isn't a record is refused as running, never run past.
    [Theory]
    [InlineData("not json")]
    [InlineData("{\"s\":201}")]
    public async Task ARecordThatUnsealsButIsNotOne_IsRefusedAsRunning(string unsealed)
    {
        var sealedRecord = _keyRing.CreateProtector("Bitwarden.Server.Sdk.Service.Idempotency.v1").Protect(unsealed);

        var refused = await SendAsync(Request(), store: new Holding(sealedRecord));

        Assert.Equal(StatusCodes.Status409Conflict, refused.Response.StatusCode);
        Assert.Equal(0, _operation.Runs);
    }

    // A record is kept per caller: another service using the same key has its own write done.
    [Fact]
    public async Task TwoCallersWithOneKey_BothRun()
    {
        await SendAsync(Request(client: "internal.gadget-service"));
        var other = await SendAsync(Request(client: "internal.widget-service"));

        Assert.False(other.Response.Headers.ContainsKey(IdempotencyKeys.ReplayedHeader));
        Assert.Equal(2, _operation.Runs);
    }

    // A record is kept per person too: a caller that sends one key for two people's requests has both done, and
    // never answers one person with the other's answer.
    [Fact]
    public async Task OneCallerAndKeyForTwoPeople_BothRun()
    {
        await SendAsync(About(Request(), user: Guid.NewGuid()));
        var other = await SendAsync(About(Request(), user: Guid.NewGuid()));

        Assert.False(other.Response.Headers.ContainsKey(IdempotencyKeys.ReplayedHeader));
        Assert.Equal(2, _operation.Runs);
    }

    [Fact]
    public async Task ARepeatAboutTheSamePerson_IsReplayed()
    {
        var organization = Guid.NewGuid();
        var user = Guid.NewGuid();

        await SendAsync(About(Request(), organization, user));
        var repeat = await SendAsync(About(Request(), organization, user));

        Assert.Equal("true", repeat.Response.Headers[IdempotencyKeys.ReplayedHeader]);
        Assert.Equal(1, _operation.Runs);
    }

    [Fact]
    public void StorageKey_DiffersByOwnersAndIsUnchangedWithoutThem()
    {
        var bare = IdempotentWrites.StorageKey(Service, Caller, "k");

        Assert.Equal(bare, IdempotentWrites.StorageKey(Service, Caller, "k", owners: null));
        Assert.NotEqual(bare, IdempotentWrites.StorageKey(Service, Caller, "k", owners: "a"));
        Assert.NotEqual(
            IdempotentWrites.StorageKey(Service, Caller, "k", owners: "a"),
            IdempotentWrites.StorageKey(Service, Caller, "k", owners: "b"));
    }

    private static DefaultHttpContext About(DefaultHttpContext http, Guid? organization = null, Guid? user = null)
    {
        http.RequestServices = new ContextServices(new Owners(organization, user));
        return http;
    }

    private async Task<HttpContext> SendAsync(
        HttpContext http,
        IIdempotencyStore? store = null,
        IDataProtectionProvider? keyRing = null)
    {
        var writes = new IdempotentWrites(
            _operation.InvokeAsync,
            store ?? _store,
            keyRing ?? _keyRing,
            new WidgetService(),
            new Settings(_settings),
            NullLogger<IdempotentWrites>.Instance);

        await writes.InvokeAsync(http);
        return http;
    }

    private static DefaultHttpContext Request(
        string method = "POST",
        string path = Path,
        string query = "",
        string body = Operation.Request,
        string? key = "key-1",
        string? client = Caller,
        bool authenticated = true,
        Endpoint? endpoint = null,
        bool withEndpoint = true)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Path = path;
        http.Request.QueryString = new QueryString(query.Length == 0 ? null : query);
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        http.Request.ContentType = "application/json";
        http.Response.Body = new MemoryStream();

        if (key is not null)
        {
            http.Request.Headers[IdempotencyKeys.Header] = key;
        }

        SecurityClaim[] claims = client is null ? [] : [new SecurityClaim(InternalCaller.ClientClaim, client)];
        http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "Bearer" : null));

        if (withEndpoint)
        {
            http.SetEndpoint(endpoint ?? Endpoint(new AcceptsIdempotencyKeyAttribute()));
        }

        return http;
    }

    private static Endpoint Endpoint(params object[] metadata) =>
        new(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "widgets");

    private static string Body(HttpContext http) =>
        Encoding.UTF8.GetString(((MemoryStream)http.Response.Body).ToArray());

    /// <summary>
    /// The operation behind the middleware: it counts its runs, reads the body, and answers as a create would.
    /// </summary>
    private sealed class Operation
    {
        public const string Request = "{\"name\":\"widget\"}";

        public const string Answer = "{\"id\":\"widget-1\",\"name\":\"widget\"}";

        private int _runs;

        public int Runs => _runs;

        public string? LastBody { get; private set; }

        public Func<HttpContext, Task> Respond { get; set; } = CreatedAsync;

        public async Task InvokeAsync(HttpContext http)
        {
            Interlocked.Increment(ref _runs);
            using var reader = new StreamReader(http.Request.Body, leaveOpen: true);
            LastBody = await reader.ReadToEndAsync(Token);
            await Respond(http);
        }

        private static async Task CreatedAsync(HttpContext http)
        {
            http.Response.StatusCode = StatusCodes.Status201Created;
            http.Response.ContentType = "application/json; charset=utf-8";
            http.Response.Headers.Location = "/api/v1/widgets/widget-1";
            http.Response.Headers.ETag = "\"v1\"";
            http.Response.Headers.LastModified = "Tue, 10 Nov 2026 12:00:00 GMT";
            http.Response.Headers["X-Widget-Trace"] = "abc";
            await http.Response.WriteAsync(Answer, Token);
        }
    }

    /// <summary>
    /// A store that counts what the middleware asks of it, and passes it on.
    /// </summary>
    private sealed class RecordingStore(IIdempotencyStore inner) : IIdempotencyStore
    {
        public int Claims { get; private set; }

        public int Completes { get; private set; }

        public int Releases { get; private set; }

        public string? LastKey { get; private set; }

        public TimeSpan? LastLease { get; private set; }

        public TimeSpan? LastRetention { get; private set; }

        public string? LastAnswer { get; private set; }

        public ValueTask<Claim> ClaimAsync(string key, string pending, TimeSpan lease)
        {
            Claims++;
            LastKey = key;
            LastLease = lease;
            return inner.ClaimAsync(key, pending, lease);
        }

        public ValueTask CompleteAsync(string key, string answer, TimeSpan retention)
        {
            Completes++;
            LastRetention = retention;
            LastAnswer = answer;
            return inner.CompleteAsync(key, answer, retention);
        }

        public ValueTask ReleaseAsync(string key, string pending)
        {
            Releases++;
            return inner.ReleaseAsync(key, pending);
        }
    }

    private sealed class Unavailable : IIdempotencyStore
    {
        public int Completes { get; private set; }

        public ValueTask<Claim> ClaimAsync(string key, string pending, TimeSpan lease) => ValueTask.FromResult(Claim.Unavailable);

        public ValueTask CompleteAsync(string key, string answer, TimeSpan retention)
        {
            Completes++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAsync(string key, string pending) => ValueTask.CompletedTask;
    }

    private sealed class Holding(string existing) : IIdempotencyStore
    {
        public ValueTask<Claim> ClaimAsync(string key, string pending, TimeSpan lease) => ValueTask.FromResult(Claim.Found(existing));

        public ValueTask CompleteAsync(string key, string answer, TimeSpan retention) => ValueTask.CompletedTask;

        public ValueTask ReleaseAsync(string key, string pending) => ValueTask.CompletedTask;
    }

    private sealed class ContextServices(IContext context) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IContext) ? context : null;
    }

    private sealed class Owners(Guid? organization, Guid? user) : IContext
    {
        public Guid? OrganizationId => organization;

        public Guid? UserId => user;

        public Guid? ProviderId => null;
    }

    private sealed class WidgetService : IServiceIdentity
    {
        public string Name => Service;

        public string Title => "Widget Service";
    }

    private sealed class Settings(IdempotencyOptions value) : IOptionsMonitor<IdempotencyOptions>
    {
        public IdempotencyOptions CurrentValue => value;

        public IdempotencyOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<IdempotencyOptions, string?> listener) => null;
    }
}
