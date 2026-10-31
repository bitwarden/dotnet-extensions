using System.Net;
using System.Text.Json;
using Bitwarden.Server.Sdk.Service.Querying;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A sparse fieldset, <c>fields[type]</c>: only the attributes asked for come back, and an operation
/// can skip working out the ones that weren't.
/// </summary>
public sealed class FieldsTests
{
    private static readonly Guid Person = new("a1000000-0000-4000-8000-00000000000b");

    [Fact]
    public void Parameter_IsNamedForTheResourceType()
    {
        Assert.Equal("fields[probe]", Fields<Probe>.Parameter);
    }

    [Fact]
    public void Attributes_AreThePublishedNamesWithoutIdAndType()
    {
        Assert.Contains("actingUser", Fields<Probe>.Attributes);
        Assert.Contains("note", Fields<Probe>.Attributes);
        Assert.DoesNotContain("id", Fields<Probe>.Attributes);
        Assert.DoesNotContain("type", Fields<Probe>.Attributes);
    }

    [Fact]
    public void Includes_NoFieldsetSent_EveryAttribute()
    {
        Assert.True(default(Fields<Probe>).Includes("note"));
    }

    [Fact]
    public void Includes_Fieldset_OnlyItsAttributes()
    {
        var fields = new Fields<Probe>(["note"]);

        Assert.True(fields.Includes("note"));
        Assert.False(fields.Includes("secret"));
    }

    [Fact]
    public void ToString_IsTheFieldsetAsSent_OrEmptyWhenNoneWas()
    {
        Assert.Equal("note", new Fields<Probe>(["note"]).ToString());
        Assert.Equal(string.Empty, default(Fields<Probe>).ToString());
    }

    [Fact]
    public void Constructor_UnknownAttribute_Refused()
    {
        var refusal = Assert.Throws<InvalidFieldsException>(() => new Fields<Probe>(["note", "nope"]));

        Assert.Equal("fields[probe]", Assert.Single(refusal.Errors).Source!.Parameter);
    }

    [Fact]
    public async Task Get_NoFieldset_EveryAttribute()
    {
        var attributes = await Attributes("/api/v1/probes/sparse");

        Assert.Equal("worked out", attributes.GetProperty("note").GetString());
        Assert.True(attributes.TryGetProperty("secret", out _));
        Assert.True(attributes.TryGetProperty("actingUser", out _));
    }

    [Fact]
    public async Task Get_Fieldset_OnlyThoseAttributes()
    {
        var attributes = await Attributes("/api/v1/probes/sparse?fields[probe]=secret,actingUser");

        Assert.False(attributes.TryGetProperty("note", out _));
        Assert.Equal("2.c|i|m", attributes.GetProperty("secret").GetString());
        Assert.Equal(Person.ToString(), attributes.GetProperty("actingUser").GetString());
    }

    [Fact]
    public async Task Get_UnknownAttribute_422AtTheParameter()
    {
        await using var service = await SessionHost.StartAsync(productService: true);
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes/sparse?fields[probe]=nope", SessionHost.Token(Person, SessionHost.Scope));

        using var response = await service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var error = body.RootElement.GetProperty("errors")[0];
        Assert.Equal("fields[probe]", error.GetProperty("source").GetProperty("parameter").GetString());
    }

    [Fact]
    public async Task OpenApi_PublishesTheFieldsetUnderItsName()
    {
        await using var service = await SessionHost.StartAsync(productService: true);

        using var response = await service.Client().GetAsync("/api/openapi.json", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var parameters = document.RootElement.GetProperty("paths").GetProperty("/api/v1/probes/sparse")
            .GetProperty("get").GetProperty("parameters").EnumerateArray().ToList();
        var fields = Assert.Single(parameters, parameter => parameter.GetProperty("name").GetString() == "fields[probe]");
        Assert.Equal("query", fields.GetProperty("in").GetString());
        Assert.Contains("note", fields.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains("Every attribute when omitted", fields.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(parameters, parameter => parameter.GetProperty("name").GetString() == "fields");
        Assert.False(document.RootElement.GetProperty("components").GetProperty("schemas").TryGetProperty("FieldsOfProbe", out _));
    }

    // Without a fieldset, everything, including what costs something to work out: a fieldset only
    // ever takes attributes away, so a caller who doesn't know to send one misses nothing.
    [Fact]
    public async Task Get_NoFieldset_CarriesEvenTheCostlyAttribute()
    {
        var attributes = await Attributes("/api/v1/probes/sparse");

        Assert.Equal("worked out at a cost", attributes.GetProperty("costly").GetString());
    }

    [Fact]
    public async Task Get_FieldsetLeavingItOut_DoesNotWorkItOut()
    {
        var attributes = await Attributes("/api/v1/probes/sparse?fields[probe]=note");

        Assert.False(attributes.TryGetProperty("costly", out _));
        Assert.Equal("worked out", attributes.GetProperty("note").GetString());
    }

    [Fact]
    public async Task List_Fieldset_AppliesToEachResource()
    {
        await using var service = await SessionHost.StartAsync(productService: true);
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes?fields[probe]=costly", SessionHost.Token(Person, SessionHost.Scope));

        using var response = await service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var resources = body.RootElement.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(2, resources.Count);
        Assert.All(resources, resource =>
        {
            var attributes = resource.GetProperty("attributes");
            Assert.Equal("worked out at a cost", attributes.GetProperty("costly").GetString());
            Assert.False(attributes.TryGetProperty("secret", out _));
        });
        Assert.Equal(2, body.RootElement.GetProperty("meta").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task List_NoFieldset_EveryAttribute()
    {
        await using var service = await SessionHost.StartAsync(productService: true);
        using var request = ProductServiceAttributeTests.Get("/api/v1/probes", SessionHost.Token(Person, SessionHost.Scope));

        using var response = await service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("costly", body, StringComparison.Ordinal);
        Assert.Contains("secret", body, StringComparison.Ordinal);
    }

    private static async Task<JsonElement> Attributes(string path)
    {
        await using var service = await SessionHost.StartAsync(productService: true);
        using var request = ProductServiceAttributeTests.Get(path, SessionHost.Token(Person, SessionHost.Scope));

        using var response = await service.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return body.RootElement.GetProperty("data").GetProperty("attributes").Clone();
    }
}
