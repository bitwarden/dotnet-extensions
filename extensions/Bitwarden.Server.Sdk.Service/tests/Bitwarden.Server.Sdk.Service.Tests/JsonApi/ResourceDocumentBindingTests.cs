using System.Text.Json;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// How a request body is validated and cleaned before it becomes a resource document.
/// </summary>
/// <remarks>
/// <para>
/// One level at a time: the document, then the resource, then its attributes, with every error at
/// the failing level reported. A body that cannot be parsed is a 400; one that can but is wrong
/// is a 422. Unknown and read-only members are dropped, not refused.
/// </para>
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
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(EnumValues.Naming) },
    };

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void TryBind_EmptyBody_BadRequest(string body)
    {
        var failure = BindTyped(body);

        Assert.Equal(400, failure!.StatusCode);
        AssertError(Assert.Single(failure.Errors), "400", ErrorCodes.MalformedJson, ErrorTitles.MalformedJson, ErrorDetails.EmptyBody, "/");
    }

    [Fact]
    public void TryBind_MalformedJson_BadRequest()
    {
        var failure = BindTyped("""{ "data": """);

        Assert.Equal(400, failure!.StatusCode);
        AssertError(Assert.Single(failure.Errors), "400", ErrorCodes.MalformedJson, ErrorTitles.MalformedJson, ErrorDetails.MalformedJson, "/");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("7")]
    [InlineData("null")]
    public void TryBind_NotAnObject_Unprocessable(string body)
    {
        var failure = BindTyped(body);

        Assert.Equal(422, failure!.StatusCode);
        AssertError(Assert.Single(failure.Errors), "422", ErrorCodes.InvalidShape, ErrorTitles.InvalidShape, ErrorDetails.NotAnObject, "/");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "data": null }""")]
    [InlineData("""{ "data": " " }""")]
    public void TryBind_NoData_Required(string body)
    {
        var failure = BindTyped(body);

        AssertError(Assert.Single(failure!.Errors), "422", ErrorCodes.FieldRequired, ErrorTitles.FieldRequired, "The 'data' field is required.", "/data");
    }

    [Theory]
    [InlineData("""{ "data": [] }""")]
    [InlineData("""{ "data": "x" }""")]
    public void TryBind_DataNotAnObject_WrongType(string body)
    {
        var failure = BindTyped(body);

        AssertError(Assert.Single(failure!.Errors), "422", ErrorCodes.InvalidType, ErrorTitles.InvalidType, "The 'data' field must be an object.", "/data");
    }

    // Both resource-level errors at once, and nothing about attributes that are not there.
    [Fact]
    public void TryBind_TypeAndAttributesMissing_BothRequired()
    {
        var failure = BindTyped("""{ "data": { "id": "1" } }""");

        Assert.Equal(422, failure!.StatusCode);
        Assert.Collection(
            failure.Errors,
            error => AssertError(error, "422", ErrorCodes.FieldRequired, ErrorTitles.FieldRequired, "The 'type' field is required.", "/data/type"),
            error => AssertError(error, "422", ErrorCodes.FieldRequired, ErrorTitles.FieldRequired, "The 'attributes' field is required.", "/data/attributes"));
    }

    [Fact]
    public void TryBind_TypeAndAttributesOfTheWrongKind_BothWrongType()
    {
        var failure = BindTyped("""{ "data": { "type": 7, "attributes": [] } }""");

        Assert.Collection(
            failure!.Errors,
            error => AssertError(error, "422", ErrorCodes.InvalidType, ErrorTitles.InvalidType, "The 'type' field must be a string.", "/data/type"),
            error => AssertError(error, "422", ErrorCodes.InvalidType, ErrorTitles.InvalidType, "The 'attributes' field must be an object.", "/data/attributes"));
    }

    [Fact]
    public void TryBind_AnotherResourceType_Refused()
    {
        var failure = BindTyped("""{ "data": { "type": "widget", "attributes": { "count": 1 } } }""");

        AssertError(
            Assert.Single(failure!.Errors),
            "422",
            ErrorCodes.InvalidResourceType,
            ErrorTitles.InvalidResourceType,
            "A resource of type 'typed-gadget' was expected, but 'widget' was declared.",
            "/data/type");
    }

    [Fact]
    public void TryBind_DocumentReadOnlyMember_Dropped()
    {
        var failure = ResourceDocumentBinding.TryBind(
            """{ "data": { "type": "typed-gadget", "attributes": { "count": 1 } }, "meta": { "note": "n" } }""",
            Options.GetTypeInfo(typeof(TypedGadget)),
            "typed-gadget",
            new HashSet<string> { "meta" },
            out var document);

        Assert.Null(failure);
        Assert.False(document!.ContainsKey("meta"));
    }

    [Fact]
    public void TryBind_UnknownAttribute_Dropped()
    {
        var failure = ResourceDocumentBinding.TryBind(
            """{ "data": { "type": "typed-gadget", "attributes": { "count": 1, "retired": "x" } } }""",
            Options.GetTypeInfo(typeof(TypedGadget)),
            "typed-gadget",
            new HashSet<string>(),
            out var document);

        Assert.Null(failure);
        Assert.False(document!["data"]!["attributes"]!.AsObject().ContainsKey("retired"));
    }

    // A non-nullable member must be sent; null counts as not sent.
    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "count": null }""")]
    public void TryBind_NonNullableAttributeAbsent_Required(string attributes)
    {
        var failure = BindTyped($$"""{ "data": { "type": "typed-gadget", "attributes": {{attributes}} } }""");

        AssertError(Assert.Single(failure!.Errors), "422", ErrorCodes.FieldRequired, ErrorTitles.FieldRequired, "The 'count' field is required.", "/data/attributes/count");
    }

    [Fact]
    public void TryBind_EveryKindOfValueRight_Accepted()
    {
        var failure = BindTyped(
            """
            { "data": { "type": "typed-gadget", "attributes": {
              "count": 1, "flag": true, "ratio": 0.5, "name": "fake", "when": "2026-01-02T03:04:05Z",
              "colour": "DARK_BLUE", "colours": ["RED"], "palette": ["DARK_BLUE"], "tags": ["a"],
              "labels": { "k": "v" }, "part": { "name": "p" }
            } } }
            """);

        Assert.Null(failure);
    }

    [Theory]
    [InlineData("flag", "\"yes\"", "a boolean")]
    [InlineData("ratio", "\"0.5\"", "a number")]
    [InlineData("ratio", "true", "a number")]
    [InlineData("name", "7", "a string")]
    [InlineData("when", "7", "a string")]
    [InlineData("colour", "7", "a string")]
    [InlineData("tags", "\"a\"", "an array")]
    [InlineData("labels", "[]", "an object")]
    [InlineData("part", "7", "an object")]
    public void TryBind_AValueOfTheWrongKind_WrongType(string member, string value, string expected)
    {
        var failure = BindTyped($$"""{ "data": { "type": "typed-gadget", "attributes": { "count": 1, "{{member}}": {{value}} } } }""");

        var error = Assert.Single(failure!.Errors, error => error.Source?.Pointer == $"/data/attributes/{member}");
        AssertError(error, "422", ErrorCodes.InvalidType, ErrorTitles.InvalidType, $"The '{member}' field must be {expected}.", $"/data/attributes/{member}");
    }

    [Fact]
    public void TryBind_AnEnumeratedValueNotPermitted_InvalidValueListingThem()
    {
        var failure = BindTyped("""{ "data": { "type": "typed-gadget", "attributes": { "count": 1, "colour": "PINK" } } }""");

        AssertError(
            Assert.Single(failure!.Errors),
            "422",
            ErrorCodes.InvalidValue,
            ErrorTitles.InvalidValue,
            "The 'colour' field must be one of: RED, DARK_BLUE.",
            "/data/attributes/colour");
    }

    // A caller sending a list is told which entries are wrong, by index.
    [Theory]
    [InlineData("colours")]
    [InlineData("palette")]
    public void TryBind_EnumeratedValuesInAListNotPermitted_EachRefusedAtItsIndex(string member)
    {
        var failure = BindTyped($$"""{ "data": { "type": "typed-gadget", "attributes": { "count": 1, "{{member}}": ["RED", "PINK", "DARK_BLUE", "TEAL"] } } }""");

        Assert.Equal(
            [$"/data/attributes/{member}/1", $"/data/attributes/{member}/3"],
            failure!.Errors.Select(error => error.Source?.Pointer));
        Assert.All(failure.Errors, error => Assert.Equal(ErrorCodes.InvalidValue, error.Code));
    }

    // Errors at the attribute level are all reported together.
    [Fact]
    public void TryBind_SeveralAttributesWrong_EveryOneReported()
    {
        var failure = BindTyped("""{ "data": { "type": "typed-gadget", "attributes": { "flag": 1, "colour": "PINK" } } }""");

        Assert.Equal(
            ["/data/attributes/flag", "/data/attributes/colour", "/data/attributes/count"],
            failure!.Errors.Select(error => error.Source?.Pointer));
    }

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

    private static DocumentFailure? BindTyped(string body) =>
        ResourceDocumentBinding.TryBind(body, Options.GetTypeInfo(typeof(TypedGadget)), "typed-gadget", new HashSet<string>(), out _);

    private static void AssertError(Error error, string status, string code, string title, string detail, string pointer)
    {
        Assert.Equal(status, error.Status);
        Assert.Equal(code, error.Code);
        Assert.Equal(title, error.Title);
        Assert.Equal(detail, error.Detail);
        Assert.Equal(pointer, error.Source?.Pointer);
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

    public enum Colour
    {
        Red,
        DarkBlue,
    }

    public sealed record TypedGadget : Resource
    {
        public int Count { get; init; }

        public bool? Flag { get; init; }

        public decimal? Ratio { get; init; }

        public string? Name { get; init; }

        public DateTimeOffset? When { get; init; }

        public Colour? Colour { get; init; }

        public List<Colour>? Colours { get; init; }

        public Colour[]? Palette { get; init; }

        public IReadOnlyList<string>? Tags { get; init; }

        public Dictionary<string, string>? Labels { get; init; }

        public Part? Part { get; init; }
    }

    public sealed record Part
    {
        public string? Name { get; init; }
    }
}
