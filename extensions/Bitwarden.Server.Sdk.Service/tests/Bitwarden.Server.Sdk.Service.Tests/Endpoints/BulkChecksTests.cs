using System.Reflection;
using System.Text.Json;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Endpoints;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A bulk request is refused before its handler runs unless it names its resources exactly one way
/// the operation accepts, its expression names only filterable fields, it asks for a mode of
/// atomicity the operation offers, and a body per resource lines up with the identifiers; the mode
/// granted is set on the request for the handler.
/// </summary>
public sealed class BulkChecksTests
{
    private static readonly QueryCollection NoQuery = new();

    [Fact]
    public void Check_NoArgument_Nothing()
    {
        BulkChecks.Check(null, Atomicity.AllOrNothing, Targeting.Either, NoQuery);
    }

    [Fact]
    public void Check_NotABulkDocument_Nothing()
    {
        BulkChecks.Check("anything", Atomicity.AllOrNothing, Targeting.Ids, Atomic("maybe"));
        BulkChecks.Check(new List<string>(), Atomicity.AllOrNothing, Targeting.Ids, Atomic("maybe"));
    }

    [Fact]
    public void Check_IdsAndFilter_RefusedAsAmbiguous()
    {
        var actual = Refused(new BulkDelete<Item> { Ids = ["a"], Filter = Filter("name") }, Targeting.Either);

        Assert.StartsWith("Name the resources with 'ids' or describe them with 'filter', not both.", actual.Message, StringComparison.Ordinal);
        Assert.Equal("/ids", actual.Pointer);
    }

    // Neither is the request somebody makes by leaving a field out, and on a delete it would mean everything.
    [Theory]
    [InlineData(Targeting.Ids, "Name the resources to act on with 'ids'.")]
    [InlineData(Targeting.Filter, "Describe the resources to act on with 'filter'.")]
    [InlineData(Targeting.Either, "Name the resources with 'ids' or describe them with 'filter'.")]
    public void Check_NeitherIdsNorFilter_RefusedSayingWhatIsAccepted(Targeting targeting, string expected)
    {
        var actual = Refused(new BulkDelete<Item>(), targeting);

        Assert.Equal(expected, actual.Message);
        Assert.Equal("/ids", actual.Pointer);
    }

    [Fact]
    public void Check_IdsWhereOnlyAFilterIsAccepted_Refused()
    {
        var actual = Refused(new BulkAction<Item> { Ids = ["a"] }, Targeting.Filter);

        Assert.Equal("This operation does not accept 'ids'. Describe the resources with 'filter'.", actual.Message);
        Assert.Equal("/ids", actual.Pointer);
    }

    [Fact]
    public void Check_FilterWhereOnlyIdsAreAccepted_Refused()
    {
        var actual = Refused(Update(filter: Filter("name")), Targeting.Ids);

        Assert.Equal("This operation does not accept 'filter'. Name the resources with 'ids'.", actual.Message);
        Assert.Equal("/filter", actual.Pointer);
    }

    [Fact]
    public void Check_FilterOnAFieldNotFilterable_Refused()
    {
        var actual = Refused(Update(filter: Filter("secret")), Targeting.Either);

        Assert.Contains("'secret' is not a field this resource may be filtered on", actual.Message, StringComparison.Ordinal);
        Assert.Equal("/filter", actual.Pointer);
    }

    [Fact]
    public void Check_FilterOnAFilterableField_Allowed()
    {
        var request = Update(filter: Filter("name"));

        BulkChecks.Check(request, Atomicity.AllOrNothing, Targeting.Either, NoQuery);

        Assert.Equal(Atomicity.AllOrNothing, request.Atomicity);
    }

    // A search names its resources one way only, but its expression is still checked.
    [Fact]
    public void Check_SearchOnAFieldNotFilterable_Refused()
    {
        var actual = Refused(new Search<Item> { Filter = Filter("secret") }, Targeting.Ids);

        Assert.Equal("/filter", actual.Pointer);
    }

    [Fact]
    public void Check_Search_NoTargetingToCheck()
    {
        BulkChecks.Check(new Search<Item> { Filter = Filter("name") }, Atomicity.AllOrNothing, Targeting.Ids, NoQuery);
    }

    // Creates and replaces carry their resources in data, so there is nothing to target.
    [Fact]
    public void Check_CreateAndReplace_NoTargetingToCheck()
    {
        var create = new BulkCreate<Item> { Data = [new Item()] };
        var replace = new BulkReplace<Item> { Data = [new Item { Id = "a" }] };

        BulkChecks.Check(create, Atomicity.BestEffort, Targeting.Ids, Atomic("false"));
        BulkChecks.Check(replace, Atomicity.BestEffort, Targeting.Ids, Atomic("false"));

        Assert.Equal(Atomicity.BestEffort, create.Atomicity);
        Assert.Equal(Atomicity.BestEffort, replace.Atomicity);
    }

    // Absent is all-or-nothing where offered, the safer of the two, and otherwise the only mode offered.
    [Theory]
    [InlineData(Atomicity.AllOrNothing, Atomicity.AllOrNothing)]
    [InlineData(Atomicity.AllOrNothing | Atomicity.BestEffort, Atomicity.AllOrNothing)]
    [InlineData(Atomicity.BestEffort, Atomicity.BestEffort)]
    public void Check_AtomicAbsent_TheSafestModeOffered(Atomicity supports, Atomicity expected)
    {
        var request = new BulkDelete<Item> { Ids = ["a"] };

        BulkChecks.Check(request, supports, Targeting.Either, NoQuery);

        Assert.Equal(expected, request.Atomicity);
    }

    [Theory]
    [InlineData("true", Atomicity.AllOrNothing)]
    [InlineData("false", Atomicity.BestEffort)]
    [InlineData("False", Atomicity.BestEffort)]
    public void Check_AtomicOffered_Granted(string atomic, Atomicity expected)
    {
        var request = new BulkDelete<Item> { Ids = ["a"] };

        BulkChecks.Check(request, Atomicity.AllOrNothing | Atomicity.BestEffort, Targeting.Either, Atomic(atomic));

        Assert.Equal(expected, request.Atomicity);
    }

    [Fact]
    public void Check_BestEffortNotOffered_Refused()
    {
        var actual = Refused(new BulkDelete<Item> { Ids = ["a"] }, Targeting.Either, Atomicity.AllOrNothing, Atomic("false"));

        Assert.StartsWith("This operation is all-or-nothing and does not support 'atomic=false'.", actual.Message, StringComparison.Ordinal);
        Assert.Equal("/atomic", actual.Pointer);
    }

    [Fact]
    public void Check_AllOrNothingNotOffered_Refused()
    {
        var actual = Refused(new BulkDelete<Item> { Ids = ["a"] }, Targeting.Either, Atomicity.BestEffort, Atomic("true"));

        Assert.StartsWith("This operation is best-effort and does not support 'atomic=true'.", actual.Message, StringComparison.Ordinal);
        Assert.Equal("/atomic", actual.Pointer);
    }

    // Malformed rather than a mode nobody offers, so refused even where both are offered.
    [Theory]
    [InlineData("yes")]
    [InlineData("")]
    [InlineData("1")]
    public void Check_AtomicNotABoolean_Refused(string atomic)
    {
        var actual = Refused(
            new BulkDelete<Item> { Ids = ["a"] }, Targeting.Either, Atomicity.AllOrNothing | Atomicity.BestEffort, Atomic(atomic));

        Assert.Equal(
            $"'{atomic}' is not a value 'atomic' accepts. It is 'true' for all-or-nothing or 'false' for best-effort.",
            actual.Message);
        Assert.Equal("/atomic", actual.Pointer);
    }

    [Fact]
    public void Check_BodyForEachResourceWithIds_Allowed()
    {
        var request = new BulkAction<Item, string> { Ids = ["a", "b"], Data = new BulkInput<string> { Each = ["x", "y"] } };

        BulkChecks.Check(request, Atomicity.AllOrNothing, Targeting.Either, NoQuery);
    }

    [Fact]
    public void Check_BodyForEachResourceWithAFilter_Refused()
    {
        var actual = Refused(
            new BulkAction<Item, string> { Filter = Filter("name"), Data = new BulkInput<string> { Each = ["x"] } },
            Targeting.Either);

        Assert.StartsWith("A body for each resource goes with 'ids'.", actual.Message, StringComparison.Ordinal);
        Assert.Equal("/data", actual.Pointer);
    }

    [Fact]
    public void Check_BodiesAndIdsDiffering_RefusedCountingBoth()
    {
        var actual = Refused(
            new BulkAction<Item, string> { Ids = ["a", "b", "c"], Data = new BulkInput<string> { Each = ["x"] } },
            Targeting.Either);

        Assert.StartsWith("3 resources were named and 1 bodies were sent.", actual.Message, StringComparison.Ordinal);
        Assert.Equal("/data", actual.Pointer);
    }

    // One body for every resource, or none, has nothing to line up.
    [Fact]
    public void Check_OneBodyForEveryResourceOrNone_Allowed()
    {
        BulkChecks.Check(
            new BulkAction<Item, string> { Filter = Filter("name"), Data = new BulkInput<string> { Every = "x" } },
            Atomicity.AllOrNothing,
            Targeting.Either,
            NoQuery);
        BulkChecks.Check(new BulkAction<Item, string> { Ids = ["a"] }, Atomicity.AllOrNothing, Targeting.Either, NoQuery);
    }

    [Fact]
    public void Declared_BulkAction_ItsModesAndTargeting()
    {
        Assert.Equal(
            (Atomicity.BestEffort, Targeting.Ids),
            BulkChecks.Declared(["other", new StandardBulkActionAttribute("api/v1/items", "confirm") { Supports = Atomicity.BestEffort, Targeting = Targeting.Ids }]));
    }

    [Fact]
    public void Declared_BulkUpdate_ItsModesAndTargeting()
    {
        Assert.Equal(
            (Atomicity.BestEffort, Targeting.Filter),
            BulkChecks.Declared([new StandardBulkUpdateAttribute("api/v1/items") { Supports = Atomicity.BestEffort, Targeting = Targeting.Filter }]));
    }

    [Fact]
    public void Declared_BulkDelete_ItsModesAndTargeting()
    {
        Assert.Equal(
            (Atomicity.BestEffort, Targeting.Ids),
            BulkChecks.Declared([new StandardBulkDeleteAttribute("api/v1/items") { Supports = Atomicity.BestEffort, Targeting = Targeting.Ids }]));
    }

    [Fact]
    public void Declared_CreateAndReplace_ItsModesTargetedEitherWay()
    {
        Assert.Equal(
            (Atomicity.BestEffort, Targeting.Either),
            BulkChecks.Declared([new StandardBulkCreateAttribute("api/v1/items") { Supports = Atomicity.BestEffort }]));
        Assert.Equal(
            (Atomicity.BestEffort, Targeting.Either),
            BulkChecks.Declared([new StandardBulkReplaceAttribute("api/v1/items") { Supports = Atomicity.BestEffort }]));
    }

    [Fact]
    public void Declared_NoBulkAttribute_AllOrNothingTargetedEitherWay()
    {
        Assert.Equal((Atomicity.AllOrNothing, Targeting.Either), BulkChecks.Declared([new StandardCreateAttribute(), "other"]));
    }

    // The startup check lets a handler take an expression only where the filter checks it, for
    // every standard operation there is: a new one has to be added to both or to neither.
    [Theory]
    [MemberData(nameof(EveryStandardOperation))]
    public async Task ChecksFilter_EveryStandardOperation_TheStartupCheckAndTheFilterAgree(string handler)
    {
        var method = typeof(EveryOperation).GetMethod(handler)!;
        var problems = new List<string>();
        StandardEndpointCheck.Unchecked(method, standardized: true, problems);

        var filter = StandardEndpointFilter.Create(
            new EndpointFilterFactoryContext { MethodInfo = method, ApplicationServices = new ServiceCollection().BuildServiceProvider() },
            _ => ValueTask.FromResult<object?>(null));
        var request = new BulkDelete<Item> { Filter = Filter("secret") };
        var refused = await Record.ExceptionAsync(
            async () => await filter(new DefaultEndpointFilterInvocationContext(new DefaultHttpContext(), request)));

        Assert.Equal(problems.Count == 0, refused is InvalidRequestException);
        Assert.Equal(problems.Count == 0, BulkChecks.ChecksFilter(Operation(method)));
    }

    [Fact]
    public void ChecksFilter_EveryStandardOperation_HasAHandlerAbove()
    {
        var operations = typeof(IStandardOperation).Assembly.GetTypes()
            .Where(type => typeof(IStandardOperation).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
            .OrderBy(type => type.Name);
        var covered = typeof(EveryOperation).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => Operation(method).GetType())
            .OrderBy(type => type.Name);

        Assert.Equal(operations, covered);
    }

    /// <summary>
    /// The name of each handler in <see cref="EveryOperation"/>.
    /// </summary>
    public static TheoryData<string> EveryStandardOperation =>
        [.. typeof(EveryOperation).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(method => method.Name)];

    private static IStandardOperation Operation(MethodInfo method) =>
        method.GetCustomAttributes(inherit: true).OfType<IStandardOperation>().Single();

    private static InvalidRequestException Refused(
        object request,
        Targeting targeting,
        Atomicity supports = Atomicity.AllOrNothing,
        IQueryCollection? query = null) =>
        Assert.Throws<InvalidRequestException>(() => BulkChecks.Check(request, supports, targeting, query ?? NoQuery));

    private static QueryCollection Atomic(string value) =>
        new(new Dictionary<string, StringValues> { ["atomic"] = value });

    private static BulkUpdate<Item> Update(IReadOnlyList<string>? ids = null, Query? filter = null) => new()
    {
        Ids = ids,
        Filter = filter,
        Update = new Dictionary<string, JsonElement>(),
    };

    private static Query Filter(string field)
    {
        using var document = JsonDocument.Parse($$"""{ "==": [{ "var": "{{field}}" }, "x"] }""");

        return Query.Parse(document.RootElement.Clone());
    }

    /// <summary>
    /// One handler for each standard operation, each taking an expression.
    /// </summary>
    private static class EveryOperation
    {
        [StandardAction]
        public static string Action(BulkDelete<Item> request) => string.Empty;

        [StandardBulkAction("api/v1/items", "confirm")]
        public static string BulkAction(BulkDelete<Item> request) => string.Empty;

        [StandardBulkCreate("api/v1/items")]
        public static string BulkCreate(BulkDelete<Item> request) => string.Empty;

        [StandardBulkDelete("api/v1/items")]
        public static string BulkDelete(BulkDelete<Item> request) => string.Empty;

        [StandardBulkReplace("api/v1/items")]
        public static string BulkReplace(BulkDelete<Item> request) => string.Empty;

        [StandardBulkUpdate("api/v1/items")]
        public static string BulkUpdate(BulkDelete<Item> request) => string.Empty;

        [StandardCreate]
        public static string Create(BulkDelete<Item> request) => string.Empty;

        [StandardDelete]
        public static string Delete(BulkDelete<Item> request) => string.Empty;

        [StandardReadMany]
        public static string ReadMany(BulkDelete<Item> request) => string.Empty;

        [StandardReadOne]
        public static string ReadOne(BulkDelete<Item> request) => string.Empty;

        [StandardSearch("api/v1/items")]
        public static string Search(BulkDelete<Item> request) => string.Empty;

        [StandardUpdate]
        public static string Update(BulkDelete<Item> request) => string.Empty;
    }

    /// <summary>
    /// A resource with one field an expression may name and one it may not.
    /// </summary>
    public sealed record Item : Resource
    {
        /// <summary>
        /// Filterable.
        /// </summary>
        [Filterable]
        public string? Name { get; init; }

        /// <summary>
        /// Not filterable.
        /// </summary>
        public string? Secret { get; init; }
    }
}
