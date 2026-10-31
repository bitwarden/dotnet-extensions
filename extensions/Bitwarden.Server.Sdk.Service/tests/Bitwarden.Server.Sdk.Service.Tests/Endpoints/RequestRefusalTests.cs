using System.Reflection;
using System.Text.Json;
using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Service.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A request that failed binding is refused as an MVC controller's <c>[ApiController]</c> refuses it:
/// in its words, in its model state's order, each failure pointing into the document or naming the
/// query parameter it is about.
/// </summary>
public sealed class RequestRefusalTests
{
    [Theory]
    [InlineData("", "/data")]
    [InlineData("$", "/data")]
    [InlineData("Data.Email", "/data/attributes/email")]
    [InlineData("$.data.email", "/data/attributes/email")]
    [InlineData("Data.Address.Zip", "/data/attributes/address/zip")]
    [InlineData("Data.Id", "/data/id")]
    [InlineData("$.data.type", "/data/type")]
    [InlineData("Data", "/data")]
    [InlineData("Data[1].Name", "/data/1/attributes/name")]
    [InlineData("$.data[1].lid", "/data/1/lid")]
    [InlineData("$.data[0].id", "/data/0/id")]
    [InlineData("Data[2]", "/data/2")]
    [InlineData("Items[Home].Number", "/items/Home/number")]
    [InlineData("Items[a/b~c]", "/items/a~1b~0c")]
    [InlineData("Items[open", "/items/open")]
    [InlineData("Name", "/name")]
    public void Pointer_Key_IntoTheWireDocument(string key, string expected)
    {
        Assert.Equal(expected, RequestRefusal.Pointer(key));
    }

    [Fact]
    public void Required_Name_AControllersWords()
    {
        Assert.Equal("The count field is required.", RequestRefusal.Required("count"));
    }

    [Fact]
    public async Task Unprocessable_Errors_InModelStateOrderWithTheirSources()
    {
        var http = Http();
        var sources = new Dictionary<string, ErrorSource> { ["q"] = new() { Parameter = "q" } };

        await RequestRefusal.Unprocessable([new("q", "Bad query."), new("Data.Email", "Bad email.")], sources).ExecuteAsync(http);

        Assert.Equal(422, http.Response.StatusCode);
        var errors = Errors(http);
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, error => error.Detail == "Bad query." && error.Parameter == "q" && error.Pointer is null);
        Assert.Contains(errors, error => error.Detail == "Bad email." && error.Pointer == "/data/attributes/email");
        Assert.All(errors, error => Assert.Equal("422", error.Status));
    }

    [Fact]
    public async Task Errors_Status_WrittenWithIt()
    {
        var http = Http();

        await RequestRefusal.Errors(409, [new Error { Status = "409", Detail = "Taken." }]).ExecuteAsync(http);

        Assert.Equal(409, http.Response.StatusCode);
        Assert.Equal("Taken.", Assert.Single(Errors(http)).Detail);
    }

    [Fact]
    public void CheckSimple_RequiredQueryValueAbsent_RefusedNamingTheParameter()
    {
        var (errors, sources) = CheckSimple("count", ValuePlace.Query, required: true, Http());

        Assert.Equal([new ModelError("count", "The count field is required.")], errors);
        Assert.Equal("count", sources["count"].Parameter);
    }

    [Fact]
    public void CheckSimple_OptionalValueAbsent_Nothing()
    {
        var (errors, _) = CheckSimple("limit", ValuePlace.Query, required: false, Http());

        Assert.Empty(errors);
    }

    [Fact]
    public void CheckSimple_RequiredHeaderAbsent_RefusedNotAsAQueryParameter()
    {
        var (errors, sources) = CheckSimple("count", ValuePlace.Header, required: true, Http(), modelName: "X-Count");

        Assert.Equal([new ModelError("X-Count", "The count field is required.")], errors);
        Assert.NotNull(sources["X-Count"].Pointer);
        Assert.Null(sources["X-Count"].Parameter);
    }

    [Fact]
    public void CheckSimple_HeaderNotAValue_Refused()
    {
        var http = Http();
        http.Request.Headers["X-Count"] = "many";

        var (errors, _) = CheckSimple("count", ValuePlace.Header, required: true, http, modelName: "X-Count");

        Assert.Equal([new ModelError("X-Count", "The value 'many' is not valid.")], errors);
    }

    [Fact]
    public void CheckSimple_RouteValueNotAValue_RefusedWithAPointer()
    {
        var http = Http();
        http.Request.RouteValues["count"] = "many";

        var (errors, sources) = CheckSimple("count", ValuePlace.RouteOrQuery, required: true, http);

        Assert.Equal([new ModelError("count", "The value 'many' is not valid.")], errors);
        Assert.Equal("/count", sources["count"].Pointer);
    }

    // Read from the route only, so a query value of the same name does not stand in for it.
    [Fact]
    public void CheckSimple_RouteOnlyValueAbsent_Required()
    {
        var http = Http("?count=1");

        var (errors, _) = CheckSimple("count", ValuePlace.Route, required: true, http);

        Assert.Equal([new ModelError("count", "The count field is required.")], errors);
    }

    [Fact]
    public void CheckSimple_RouteOrQueryNotInTheRoute_ReadFromTheQuery()
    {
        var (errors, sources) = CheckSimple("count", ValuePlace.RouteOrQuery, required: true, Http("?count=x"));

        Assert.Equal([new ModelError("count", "The value 'x' is not valid.")], errors);
        Assert.Equal("count", sources["count"].Parameter);
    }

    // A single value is the last sent, as a controller's binder takes it.
    [Theory]
    [InlineData("?count=x&count=2", false)]
    [InlineData("?count=2&count=x", true)]
    public void CheckSimple_SentTwice_TheLastChecked(string query, bool refused)
    {
        var (errors, _) = CheckSimple("count", ValuePlace.Query, required: true, Http(query));

        Assert.Equal(refused, errors.Count > 0);
    }

    [Fact]
    public void CheckSimple_EmptyForAValueThatCannotBeNull_Invalid()
    {
        var (errors, _) = CheckSimple("count", ValuePlace.Query, required: true, Http("?count="));

        Assert.Equal([new ModelError("count", "The value '' is invalid.")], errors);
    }

    [Theory]
    [InlineData("limit")]
    [InlineData("name")]
    public void CheckSimple_EmptyForAValueThatCanBeNull_Nothing(string name)
    {
        var (errors, _) = CheckSimple(name, ValuePlace.Query, required: false, Http($"?{name}="));

        Assert.Empty(errors);
    }

    // Each of an array's values is checked, and the first that fails is the one named.
    [Fact]
    public void CheckSimple_ArrayWithBadValues_TheFirstNamed()
    {
        var (errors, _) = CheckSimple("ids", ValuePlace.Query, required: false, Http("?ids=1&ids=&ids=x&ids=y"));

        Assert.Equal([new ModelError("ids", "The value 'x' is not valid.")], errors);
    }

    [Fact]
    public void CheckMembers_QueryModel_EachMemberThatDoesNotParseByItsSentName()
    {
        var http = Http("?n=many&Flag=maybe&Label=anything&Child=x");
        http.Request.RouteValues["Size"] = "huge";
        http.Request.Headers["X-Level"] = "high";
        var errors = new List<ModelError>();

        RequestRefusal.CheckMembers(http, Parameter(nameof(Handlers.Members), "query"), errors, []);

        Assert.Equal(
            [
                new ModelError("n", "The value 'many' is not valid for Count."),
                new ModelError("Flag", "The value 'maybe' is not valid for Flag."),
                new ModelError("Size", "The value 'huge' is not valid for Size."),
                new ModelError("X-Level", "The value 'high' is not valid for Level."),
            ],
            errors);
    }

    [Fact]
    public void CheckMembers_FormModel_ReadFromTheForm()
    {
        var http = Form(new() { ["n"] = "many" });
        var errors = new List<ModelError>();

        RequestRefusal.CheckMembers(http, Parameter(nameof(Handlers.Members), "form"), errors, []);

        Assert.Equal([new ModelError("n", "The value 'many' is not valid for Count.")], errors);
    }

    [Fact]
    public void CheckMembers_FormModelWithoutAForm_Nothing()
    {
        var errors = new List<ModelError>();

        RequestRefusal.CheckMembers(Http("?n=many"), Parameter(nameof(Handlers.Members), "form"), errors, []);

        Assert.Empty(errors);
    }

    [Fact]
    public void FillFromForm_FieldsByTheirFormNames_Converted()
    {
        var model = new Model { Count = 1, Label = "old", Flag = true, Size = 3 };

        RequestRefusal.FillFromForm(
            Form(new() { ["n"] = "7", ["Label"] = "new", ["Flag"] = " ", ["Size"] = "", ["Level"] = "x" }),
            Parameter(nameof(Handlers.Members), "form"),
            model);

        Assert.Equal(7, model.Count);
        Assert.Equal("new", model.Label);
        Assert.Null(model.Flag);
        Assert.Equal(3, model.Size);
        Assert.Null(model.Level);
    }

    // What does not convert is left for validation to report.
    [Fact]
    public void FillFromForm_FieldNotAValue_LeftAsItWas()
    {
        var model = new Model { Count = 1 };

        RequestRefusal.FillFromForm(Form(new() { ["n"] = "many" }), Parameter(nameof(Handlers.Members), "form"), model);

        Assert.Equal(1, model.Count);
    }

    [Fact]
    public void FillFromForm_BlankString_None()
    {
        var model = new Model { Label = "old" };

        RequestRefusal.FillFromForm(Form(new() { ["Label"] = "" }), Parameter(nameof(Handlers.Members), "form"), model);

        Assert.Null(model.Label);
    }

    [Fact]
    public void FillFromForm_NotAFormModel_Untouched()
    {
        var model = new Model { Count = 1 };

        RequestRefusal.FillFromForm(Form(new() { ["n"] = "7" }), Parameter(nameof(Handlers.Members), "query"), model);

        Assert.Equal(1, model.Count);
    }

    [Fact]
    public void FillFromForm_NoForm_Untouched()
    {
        var model = new Model { Count = 1 };

        RequestRefusal.FillFromForm(Http("?n=7"), Parameter(nameof(Handlers.Members), "form"), model);

        Assert.Equal(1, model.Count);
    }

    private static (List<ModelError> Errors, Dictionary<string, ErrorSource> Sources) CheckSimple(
        string name, ValuePlace place, bool required, HttpContext http, string? modelName = null)
    {
        var info = Info(nameof(Handlers.Simple), name);
        var errors = new List<ModelError>();
        var sources = new Dictionary<string, ErrorSource>();

        RequestRefusal.CheckSimple(
            http, new RequestParameter(info.Position, info, ParameterKind.Simple, modelName ?? name, place, required), errors, sources);

        return (errors, sources);
    }

    private static ParameterInfo Info(string method, string name) =>
        typeof(Handlers).GetMethod(method)!.GetParameters().Single(parameter => parameter.Name == name);

    private static RequestParameter Parameter(string method, string name)
    {
        var info = Info(method, name);

        return new RequestParameter(info.Position, info, ParameterKind.Model, name, ValuePlace.RouteOrQuery, Required: true);
    }

    private static DefaultHttpContext Http(string query = "")
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddOptions().BuildServiceProvider(),
        };
        http.Request.QueryString = new QueryString(query);
        http.Response.Body = new MemoryStream();

        return http;
    }

    private static DefaultHttpContext Form(Dictionary<string, StringValues> fields)
    {
        var http = Http();
        http.Request.ContentType = "application/x-www-form-urlencoded";
        http.Request.Form = new FormCollection(fields);

        return http;
    }

    private static List<(string? Status, string? Detail, string? Pointer, string? Parameter)> Errors(HttpContext http)
    {
        http.Response.Body.Position = 0;
        using var document = JsonDocument.Parse(http.Response.Body);

        return
        [
            .. document.RootElement.GetProperty("errors").EnumerateArray().Select(error => (
                error.TryGetProperty("status", out var status) ? status.GetString() : null,
                error.TryGetProperty("detail", out var detail) ? detail.GetString() : null,
                error.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object && source.TryGetProperty("pointer", out var pointer) ? pointer.GetString() : null,
                error.TryGetProperty("source", out var named) && named.ValueKind == JsonValueKind.Object && named.TryGetProperty("parameter", out var parameter) ? parameter.GetString() : null)),
        ];
    }

    /// <summary>
    /// A model bound member by member.
    /// </summary>
    public sealed class Model
    {
        /// <summary>
        /// Sent as <c>n</c>.
        /// </summary>
        [FromForm(Name = "n")]
        [FromQuery(Name = "n")]
        public int Count { get; set; }

        /// <summary>
        /// Sent by its own name.
        /// </summary>
        public string? Label { get; set; }

        /// <summary>
        /// A nullable value.
        /// </summary>
        public bool? Flag { get; set; }

        /// <summary>
        /// A value that cannot be null, from the route.
        /// </summary>
        public int Size { get; set; }

        /// <summary>
        /// From a header.
        /// </summary>
        [FromHeader(Name = "X-Level")]
        public int? Level { get; set; }

        /// <summary>
        /// Not a simple value, so never checked here.
        /// </summary>
        public Model? Child { get; set; }

        /// <summary>
        /// Read-only, so never bound.
        /// </summary>
        public int Computed => Count * 2;
    }

    /// <summary>
    /// Handlers whose parameters are checked.
    /// </summary>
    public static class Handlers
    {
#pragma warning disable IDE0060
        /// <summary>
        /// Simple values.
        /// </summary>
        public static void Simple(int count, int? limit, string? name, int[] ids)
        {
        }

        /// <summary>
        /// Models.
        /// </summary>
        public static void Members([AsParameters] Model query, [FromForm] Model form)
        {
        }
#pragma warning restore IDE0060
    }
}
