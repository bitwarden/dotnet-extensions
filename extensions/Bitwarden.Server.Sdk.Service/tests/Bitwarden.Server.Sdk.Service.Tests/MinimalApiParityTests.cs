using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;

namespace Bitwarden.Server.Sdk.Service.Tests;

/// <summary>
/// Handlers under <c>WithStandards()</c> answer every request in the matrix exactly as recorded:
/// status, headers and body, byte for byte, and publish the recorded OpenAPI document.
/// </summary>
/// <remarks>
/// <para>
/// <c>MinimalApiParity.golden.txt</c> holds the answers an MVC controller gives the same operations,
/// but for one deliberate difference: a request sent with an unsupported media type, which a
/// controller answers with an RFC 9457 problem document, is answered with the standard error document
/// every other failure uses. An optional body sent as nothing at all, with no media type, is answered
/// as a controller's binder answers an optional body (<c>EmptyBodyBehavior.Allow</c>): none.
/// <c>MinimalApiParity.openapi.golden.json</c> holds the OpenAPI document the handlers publish,
/// including <c>parity-raw/unread</c> (<see cref="Endpoints.UnreadableRequestBodyTests"/>).
/// Rerun with <c>RECORD_PARITY=1</c> to record a deliberate change.
/// </para>
/// <para>
/// The request matrix leans on what is hardest to get right: bodies that cannot be read or do not
/// validate, query, route and header values that do not parse, the platform's query types, and a
/// bulk operation's checks. A handler refusing a request in other words would be a change a caller
/// can see.
/// </para>
/// </remarks>
public sealed class MinimalApiParityTests(MinimalApiParityTests.Host host) : IClassFixture<MinimalApiParityTests.Host>
{
    private const string Items = "/api/v1/parity-items";

    /// <summary>
    /// The probe service.
    /// </summary>
    public sealed class Host : IAsyncLifetime
    {
        public SessionHost Service { get; private set; } = null!;

        public async ValueTask InitializeAsync() =>
            Service = await SessionHost.StartAsync(productService: false);

        public async ValueTask DisposeAsync() => await Service.DisposeAsync();
    }

    private static readonly string LongText = new('x', 300);

    /// <summary>
    /// Each case: a name, then the method, path, content type and body.
    /// </summary>
    private static readonly (string Name, string Method, string Path, string? ContentType, string? Body)[] Matrix =
    [
        // A resource document.
        ("create valid", "POST", Items, "application/json", Document("""{"name":"ok","quantity":2}""")),
        ("create invalid everywhere", "POST", Items, "application/json",
            Document($$$"""{"name":"far too long a name","quantity":0,"note":"{{{LongText}}}","address":{"zip":"1234567"},"others":[{"city":"a"},{"zip":"1234567"}],"secret":"{{{LongText}}}"}""")),
        ("create name missing", "POST", Items, "application/json", Document("""{"quantity":2}""")),
        ("create malformed", "POST", Items, "application/json", "{not json"),
        ("create empty", "POST", Items, "application/json", ""),
        ("create no data", "POST", Items, "application/json", """{"meta":{}}"""),
        ("create wrong type", "POST", Items, "application/json", """{"data":{"type":"other","attributes":{"name":"ok"}}}"""),
        ("create wrong attribute type", "POST", Items, "application/json", Document("""{"name":"ok","quantity":"two"}""")),
        ("create unknown attribute", "POST", Items, "application/json", Document("""{"name":"ok","quantity":2,"colour":"red"}""")),
        ("create as text", "POST", Items, "text/plain", Document("""{"name":"ok","quantity":2}""")),
        ("update valid", "PUT", $"{Items}/abc", "application/json", Document("""{"name":"ok","quantity":2}""")),
        ("update invalid", "PUT", $"{Items}/abc", "application/json", Document("""{"name":"ok","quantity":200}""")),

        // A plain body.
        ("submit valid", "POST", $"{Items}/abc/actions/submit", "application/json", """{"subject":"hi","count":2}"""),
        ("submit invalid everywhere", "POST", $"{Items}/abc/actions/submit", "application/json",
            """{"count":0,"email":"not an address","address":{"zip":"1"},"items":[{"city":"a"},{}]}"""),
        ("submit empty subject", "POST", $"{Items}/abc/actions/submit", "application/json", """{"subject":"","count":2}"""),
        ("submit blank subject", "POST", $"{Items}/abc/actions/submit", "application/json", """{"subject":"  ","count":2}"""),
        ("form empty grant", "POST", $"{Items}/form", "application/x-www-form-urlencoded", "grant_type=&scope=api"),
        ("submit seven", "POST", $"{Items}/abc/actions/submit", "application/json", """{"subject":"hi","count":7}"""),
        ("submit malformed", "POST", $"{Items}/abc/actions/submit", "application/json", "{nope"),
        ("submit mismatched", "POST", $"{Items}/abc/actions/submit", "application/json", """{"subject":"hi","count":"two"}"""),
        ("submit nested mismatch", "POST", $"{Items}/abc/actions/submit", "application/json", """{"subject":"hi","count":2,"items":[{"city":1}]}"""),
        ("submit empty", "POST", $"{Items}/abc/actions/submit", "application/json", ""),
        ("submit null", "POST", $"{Items}/abc/actions/submit", "application/json", "null"),
        ("submit as text", "POST", $"{Items}/abc/actions/submit", "text/plain", """{"subject":"hi","count":2}"""),
        ("submit as text/json", "POST", $"{Items}/abc/actions/submit", "text/json", """{"subject":"hi","count":2}"""),
        ("submit no content type", "POST", $"{Items}/abc/actions/submit", null, """{"subject":"hi","count":2}"""),
        ("submit optionally empty", "POST", $"{Items}/abc/actions/submit-optionally", "application/json", ""),
        ("submit optionally valid", "POST", $"{Items}/abc/actions/submit-optionally", "application/json", """{"subject":"hi","count":2}"""),
        ("submit optionally invalid", "POST", $"{Items}/abc/actions/submit-optionally", "application/json", """{"count":2}"""),
        ("submit optionally nothing at all", "POST", $"{Items}/abc/actions/submit-optionally", null, null),
        ("submit optionally empty, no content type", "POST", $"{Items}/abc/actions/submit-optionally", null, ""),

        // Query, route and header values, and the platform's query types.
        ("list", "GET", Items, null, null),
        ("list filtered and paged", "GET", $"{Items}?filter[id]=a0000000-0000-4000-8000-0000000000aa&filter[tag]=a,b&sort=-name&page[number]=2&page[size]=1&permanent=true&grant=b0000000-0000-4000-8000-0000000000bb&since=2026-01-02T03:04:05Z", null, null),
        ("list range", "GET", $"{Items}?filter[createdAt]=[2026-01-01T00:00:00Z,2026-02-01T00:00:00Z)", null, null),
        ("list bad range", "GET", $"{Items}?filter[createdAt]=[2026-01-01", null, null),
        ("list bad filter", "GET", $"{Items}?filter[id]=nope", null, null),
        ("list filter twice", "GET", $"{Items}?filter[id]=a0000000-0000-4000-8000-0000000000aa&filter[id]=a0000000-0000-4000-8000-0000000000ab", null, null),
        ("list bad sort", "GET", $"{Items}?sort=colour", null, null),
        ("list bad page number", "GET", $"{Items}?page[number]=abc", null, null),
        ("list page zero", "GET", $"{Items}?page[number]=0", null, null),
        ("list page too big", "GET", $"{Items}?page[size]=5000", null, null),
        ("list bad flag", "GET", $"{Items}?permanent=maybe", null, null),
        ("list bad grant", "GET", $"{Items}?grant=nope", null, null),
        ("list bad moment", "GET", $"{Items}?since=yesterday", null, null),
        ("list two bad values", "GET", $"{Items}?permanent=maybe&grant=nope", null, null),
        ("list bad value and bad page", "GET", $"{Items}?permanent=maybe&page[number]=abc", null, null),
        ("list empty flag", "GET", $"{Items}?permanent=", null, null),
        ("required filter missing", "GET", $"{Items}/required", null, null),
        ("required filter bad", "GET", $"{Items}/required?filter[organizationId]=nope", null, null),
        ("required filter given", "GET", $"{Items}/required?filter[organizationId]=a0000000-0000-4000-8000-0000000000aa", null, null),
        ("guid route valid", "GET", $"{Items}/by-guid/a0000000-0000-4000-8000-0000000000aa", null, null),
        ("guid route bad", "GET", $"{Items}/by-guid/nope", null, null),
        ("read sparse", "GET", $"{Items}/abc?fields[parity-item]=name,quantity", null, null),
        ("read bad fields", "GET", $"{Items}/abc?fields[parity-item]=colour", null, null),
        ("read missing", "GET", $"{Items}/missing", null, null),
        ("delete", "DELETE", $"{Items}/abc", null, null),

        // A bulk operation's checks.
        ("bulk delete by ids", "POST", $"{Items}:bulk-delete", "application/json", """{"ids":["a","b"]}"""),
        ("bulk delete by filter", "POST", $"{Items}:bulk-delete", "application/json", """{"filter":{"==":[{"var":"name"},"x"]}}"""),
        ("bulk delete by neither", "POST", $"{Items}:bulk-delete", "application/json", "{}"),
        ("bulk delete best effort", "POST", $"{Items}:bulk-delete?atomic=false", "application/json", """{"ids":["a"]}"""),
        ("bulk delete bad atomic", "POST", $"{Items}:bulk-delete?atomic=perhaps", "application/json", """{"ids":["a"]}"""),
        ("bulk delete malformed", "POST", $"{Items}:bulk-delete", "application/json", "["),

        // A raw upload.
        ("upload octets", "PUT", $"{Items}/abc/file", "application/octet-stream", "0123456789"),
        ("upload as json", "PUT", $"{Items}/abc/file", "application/json", "{}"),

        // A form, and query values taken together.
        ("form valid", "POST", $"{Items}/form", "application/x-www-form-urlencoded", "grant_type=password&scope=api"),
        ("form missing a value", "POST", $"{Items}/form", "application/x-www-form-urlencoded", "scope=api"),
        ("form too long", "POST", $"{Items}/form", "application/x-www-form-urlencoded", "grant_type=password&scope=far-too-long-a-scope"),
        ("query model valid", "GET", $"{Items}/by-query?code=ab&count=2", null, null),
        ("query model invalid", "GET", $"{Items}/by-query?code=abcdefg&count=9", null, null),
        ("query model unparseable", "GET", $"{Items}/by-query?count=many", null, null),

        // Handlers that accept invalid requests: nothing refused on their behalf, their own filters.
        ("raw echo valid", "POST", "/api/v1/parity-raw/echo", "application/json", """{"subject":"hi","count":2}"""),
        ("raw echo invalid", "POST", "/api/v1/parity-raw/echo", "application/json", """{"count":99}"""),
        ("raw echo malformed", "POST", "/api/v1/parity-raw/echo", "application/json", "{nope"),
        ("raw echo empty", "POST", "/api/v1/parity-raw/echo", "application/json", ""),
        ("raw echo as text", "POST", "/api/v1/parity-raw/echo", "text/plain", "hello"),
        ("raw form", "POST", "/api/v1/parity-raw/form", "application/x-www-form-urlencoded", "grant_type=password"),
        ("raw form empty", "POST", "/api/v1/parity-raw/form", "application/x-www-form-urlencoded", "scope=x"),
        ("raw echo nothing at all", "POST", "/api/v1/parity-raw/echo", null, ""),
        ("submit nothing at all", "POST", $"{Items}/abc/actions/submit", null, ""),
        ("raw nothing", "POST", "/api/v1/parity-raw/nothing", null, null),
        ("raw fault", "GET", "/api/v1/parity-raw/fault", null, null),
        ("raw gated open", "POST", "/api/v1/parity-raw/gated", "application/json", """{"subject":"through"}"""),
        ("raw gated closed", "POST", "/api/v1/parity-raw/gated?gate=closed", "application/json", "{nope"),

        // The probes.
        ("probe create too long", "POST", "/api/v1/probes", "application/json", "{\"data\":{\"type\":\"probe\",\"attributes\":{\"note\":\"" + LongText + "\"}}}"),
        ("probe list", "GET", "/api/v1/probes?page[size]=1&fields[probe]=note", null, null),
        ("probe mark", "POST", "/api/v1/probes/current/actions/mark", "application/json", "{}"),
        ("unknown path", "GET", "/api/v1/nothing-here", null, null),
        ("wrong verb", "PATCH", $"{Items}/abc", "application/json", "{}"),
    ];

    [Fact]
    public async Task Matrix_AnsweredAsRecorded()
    {
        var answers = new StringBuilder();

        foreach (var (name, method, path, contentType, body) in Matrix)
        {
            answers.Append(name).Append('\n').Append(await Send(host.Service, method, path, contentType, body)).Append('\n');
        }

        var golden = Path.Combine(Directory(), "MinimalApiParity.golden.txt");

        if (System.Environment.GetEnvironmentVariable("RECORD_PARITY") == "1")
        {
            await File.WriteAllTextAsync(golden, answers.ToString(), TestContext.Current.CancellationToken);
            return;
        }

        var expected = Cases(await File.ReadAllTextAsync(golden, TestContext.Current.CancellationToken));
        var actual = Cases(answers.ToString());

        Assert.Equal(expected.Select(each => each.Name), actual.Select(each => each.Name));

        foreach (var ((name, controller), (_, handler)) in expected.Zip(actual))
        {
            Assert.True(controller == handler, $"{name}:\n  controller: {controller}\n  handler:    {handler}");
        }
    }

    /// <summary>
    /// The OpenAPI document describes each operation as recorded.
    /// </summary>
    [Fact]
    public async Task OpenApi_DescribedAsRecorded()
    {
        var actual = await Document(host.Service);
        var golden = Path.Combine(Directory(), "MinimalApiParity.openapi.golden.json");

        if (System.Environment.GetEnvironmentVariable("RECORD_PARITY") == "1")
        {
            await File.WriteAllTextAsync(golden, actual, TestContext.Current.CancellationToken);
            return;
        }

        Assert.Equal(await File.ReadAllTextAsync(golden, TestContext.Current.CancellationToken), actual);
    }

    /// <summary>
    /// The recorded file's cases: a name line, then the answer line.
    /// </summary>
    private static List<(string Name, string Answer)> Cases(string recorded)
    {
        var lines = recorded.TrimEnd('\n').Split('\n');

        return [.. Enumerable.Range(0, lines.Length / 2).Select(i => (lines[2 * i], lines[(2 * i) + 1]))];
    }

    private static string Directory([CallerFilePath] string path = "") => System.IO.Path.GetDirectoryName(path)!;

    private static async Task<string> Document(SessionHost host)
    {
        using var response = await host.Client().GetAsync("/api/openapi.json", TestContext.Current.CancellationToken);
        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        return document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string Document(string attributes) => "{\"data\":{\"type\":\"parity-item\",\"attributes\":" + attributes + "}}";

    private static async Task<string> Send(SessionHost host, string method, string path, string? contentType, string? body)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", SessionHost.Token(null, SessionHost.Scope, SessionHost.WriteScope));
        request.Headers.Add("X-Request-Email", "someone@example.com");

        if (path.Contains("gate=closed", StringComparison.Ordinal))
        {
            request.Headers.Add("X-Gate", "closed");
        }

        if (body is not null)
        {
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));

            if (contentType is not null)
            {
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            }
        }

        using var response = await host.Client().SendAsync(request, TestContext.Current.CancellationToken);

        var answer = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        return $"{(int)response.StatusCode} {response.Content.Headers.ContentType} {response.Headers.Location} {answer}";
    }
}
