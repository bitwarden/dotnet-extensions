using System.Text.Json;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// How a resource document's attributes bind to a member.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="VerbatimAttribute">verbatim</see> member reaches the operation as sent: not trimmed,
/// and a blank value kept. Every other string member is still trimmed, and a blank one dropped.
/// </para>
/// <para>
/// A free-form member (<see cref="JsonElement"/>, <see cref="JsonNode"/>) takes any JSON value, not
/// only an object, so a member such as a rule's list of conditions can be an array.
/// </para>
/// </remarks>
public sealed class ResourceDocumentBindingTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("  padded  ")]
    public void TryBind_VerbatimMember_KeptAsSent(string value)
    {
        var attributes = BindVerbatim($$"""{ "data": { "type": "gadget", "attributes": { "exact": "{{value}}" } } }""");

        Assert.Equal(value, attributes["exact"]?.GetValue<string>());
    }

    [Fact]
    public void TryBind_VerbatimMemberNull_StillAbsent()
    {
        var attributes = BindVerbatim("""{ "data": { "type": "gadget", "attributes": { "exact": null } } }""");

        Assert.False(attributes.ContainsKey("exact"));
    }

    [Fact]
    public void TryBind_OrdinaryMember_TrimmedAndBlankDropped()
    {
        var attributes = BindVerbatim("""{ "data": { "type": "gadget", "attributes": { "cleaned": "  padded  ", "blank": "   " } } }""");

        Assert.Equal("padded", attributes["cleaned"]?.GetValue<string>());
        Assert.False(attributes.ContainsKey("blank"));
    }

    [Theory]
    [InlineData("""[{"kind":"human_approval"}]""")]
    [InlineData("""{"kind":"human_approval"}""")]
    [InlineData("\"text\"")]
    [InlineData("7")]
    [InlineData("true")]
    public void TryBind_AnyJsonInAFreeFormMember_Accepted(string value)
    {
        var failure = BindFreeForm($$"""{ "data": { "type": "gadget", "attributes": { "element": {{value}}, "node": {{value}} } } }""");

        Assert.Null(failure);
    }

    [Fact]
    public void TryBind_AnArrayInAnObjectMember_StillRefused()
    {
        var failure = BindFreeForm("""{ "data": { "type": "gadget", "attributes": { "part": [] } } }""");

        Assert.NotNull(failure);
        Assert.Equal("/data/attributes/part", failure.Errors[0].Source?.Pointer);
    }

    private static JsonObject BindVerbatim(string body)
    {
        var failure = ResourceDocumentBinding.TryBind(body, Options.GetTypeInfo(typeof(VerbatimGadget)), "gadget", new HashSet<string>(), out var document);

        Assert.Null(failure);
        return document!["data"]!["attributes"]!.AsObject();
    }

    private static DocumentFailure? BindFreeForm(string body) =>
        ResourceDocumentBinding.TryBind(body, Options.GetTypeInfo(typeof(FreeFormGadget)), "gadget", new HashSet<string>(), out _);

    public sealed record VerbatimGadget : Resource
    {
        [Verbatim]
        public string? Exact { get; init; }

        public string? Cleaned { get; init; }

        public string? Blank { get; init; }
    }

    public sealed record FreeFormGadget : Resource
    {
        public JsonElement? Element { get; init; }

        public JsonNode? Node { get; init; }

        public Part? Part { get; init; }
    }

    public sealed record Part
    {
        public string? Name { get; init; }
    }
}
