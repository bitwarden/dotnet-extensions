using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Reflection.Emit;
using Bitwarden.Server.Sdk.Data;
using Bitwarden.Server.Sdk.Resources.Endpoints;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Resources.Tests.Endpoints;

/// <summary>
/// A model is validated as a controller validates one: a dictionary's values keyed by the entry's
/// own key, a resource's strings bounded unless they declare a bound, at most 200 failures, and no
/// deeper than 32 levels.
/// </summary>
public sealed class ModelValidationTests
{
    private static readonly IServiceProvider Services = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public void Validate_DictionaryOfValidValues_Passes()
    {
        var errors = Validate(new Order { Lines = { ["Home"] = new Line { Number = "1" } } });

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_DictionaryValueMissingARequiredMember_RefusedUnderItsKey()
    {
        var errors = Validate(new Order
        {
            Lines = { ["Work"] = new Line { Number = "1" }, ["Home"] = new Line() },
        });

        var error = Assert.Single(errors);
        Assert.Equal("Lines[Home].Number", error.Key);
        Assert.Equal("The Number field is required.", error.Message);
    }

    [Fact]
    public void Validate_DictionaryOfPlainValues_NothingToValidateInside()
    {
        var errors = Validate(new Order { Tags = { ["colour"] = "red" } });

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("Lines[Home].Number", "/lines/Home/number")]
    [InlineData("Data.Lines[Home].Number", "/data/attributes/lines/Home/number")]
    [InlineData("Lines[a/b~c].Number", "/lines/a~1b~0c/number")]
    [InlineData("Data[1].Name", "/data/1/attributes/name")]
    [InlineData("$.data[1].lid", "/data/1/lid")]
    public void Pointer_DictionaryKey_KeptAsSent(string key, string pointer)
    {
        Assert.Equal(pointer, RequestPointer.For(key));
    }

    [Fact]
    public void Validate_ResourceStringAtTheDefaultBound_Passes()
    {
        Assert.Empty(Validate(new Widget { Name = new string('a', 255) }));
    }

    [Fact]
    public void Validate_ResourceStringPastTheDefaultBound_Refused()
    {
        var error = Assert.Single(Validate(new Widget { Name = new string('a', 256) }));

        Assert.Equal("Name", error.Key);
        Assert.Equal("The field Name must be a string with a maximum length of 255.", error.Message);
    }

    [Fact]
    public void Validate_EncryptedPastItsDeclaredBound_Refused()
    {
        var error = Assert.Single(Validate(new Vault { Short = new string('a', 51) }));

        Assert.Equal("Short", error.Key);
        Assert.Equal("The field Short must be an encrypted string of at most 50 characters.", error.Message);
    }

    // The declared bound replaces the default rather than adding to it.
    [Fact]
    public void Validate_EncryptedLongerThanTheDefaultUnderItsDeclaredBound_Passes()
    {
        Assert.Empty(Validate(new Vault { Long = new string('a', 300) }));
    }

    // Refused when the resource is described, before any member of the request is checked: the
    // declaration is wrong whatever a caller sends.
    [Fact]
    public void Validate_EncryptedWithoutABound_RefusedBeforeAnyMemberIsChecked()
    {
        var errors = new List<ModelError>();

        Assert.Throws<InvalidOperationException>(
            () => ModelValidation.Validate(new Unbounded { Name = new string('a', 256) }, string.Empty, Services, errors));
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_MoreFailingMembersThanTheCap_200Recorded()
    {
        var model = Activator.CreateInstance(ManyRequiredMembers(250))!;

        var errors = Validate(model);

        Assert.Equal(200, errors.Count);
        Assert.Equal("Member0", errors[0].Key);
        Assert.Equal("Member199", errors[^1].Key);
    }

    // Each item's own rules add a failure, which the member loop's check never sees.
    [Fact]
    public void Validate_ListOfMoreFailingItemsThanTheCap_200Recorded()
    {
        var errors = Validate(new Batch { Items = [.. Enumerable.Range(0, 300).Select(_ => new Refusing())] });

        Assert.Equal(200, errors.Count);
        Assert.Equal("Items[0]", errors[0].Key);
        Assert.Equal("Items[199]", errors[^1].Key);
    }

    // A controller counts the model as level one and validates down to level 32: a chain of 31
    // links below the model is the deepest it reaches.
    [Fact]
    public void Validate_FailureAtTheDepthLimit_Refused()
    {
        var error = Assert.Single(Validate(Chain(30)));

        Assert.Equal(string.Join('.', Enumerable.Repeat("Next", 30)) + ".Number", error.Key);
    }

    // One level further, a controller throws rather than validate, and the host answers 500.
    [Fact]
    public void Validate_FailurePastTheDepthLimit_Throws()
    {
        var actual = Assert.Throws<InvalidOperationException>(() => Validate(Chain(31)));

        Assert.Equal(
            $"Validation exceeded the maximum depth of 32 at '{string.Join('.', Enumerable.Repeat("Next", 31))}.Number'. "
            + "The model is nested too deeply.",
            actual.Message);
    }

    // Whether anything down there is wrong does not matter: reaching past the limit is the refusal.
    [Fact]
    public void Validate_ValidValuePastTheDepthLimit_Throws()
    {
        var chain = Chain(31);
        Last(chain).Number = "1";

        Assert.Throws<InvalidOperationException>(() => Validate(chain));
    }

    // A list's objects sit a level below it, as a member does; its text does not count, as a
    // controller reads that without descending.
    [Fact]
    public void Validate_ListOfObjectsPastTheDepthLimit_Throws()
    {
        var chain = Chain(30);
        var last = Last(chain);
        last.Number = "1";
        last.Links = [new Link { Number = "1" }];

        Assert.Throws<InvalidOperationException>(() => Validate(chain));
    }

    // A value made only of lists, or of dictionaries and lists, has no member to stop at: each way
    // down checks the limit itself. (A dictionary of itself is read as a map of plain values and not
    // descended into.)
    [Fact]
    public void Validate_ListsOfListsPastTheDepthLimit_Throws()
    {
        var actual = Assert.Throws<InvalidOperationException>(() => Validate(new Nest { Lists = Nested<Lists>(40) }));

        Assert.StartsWith("Validation exceeded the maximum depth of 32 at 'Lists[0]", actual.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_DictionariesAndListsPastTheDepthLimit_Throws()
    {
        var actual = Assert.Throws<InvalidOperationException>(() => Validate(new Nest { Maps = NestedMaps(40) }));

        // The dictionaries sit at odd depths, so the one at depth 31 is where the walk stops.
        var key = "Maps" + string.Concat(Enumerable.Repeat("[k][0]", 15)) + "[k]";
        Assert.Equal($"Validation exceeded the maximum depth of 32 at '{key}'. The model is nested too deeply.", actual.Message);
    }

    [Fact]
    public void Validate_ListOfTextAtTheDepthLimit_Passes()
    {
        var chain = Chain(30);
        var last = Last(chain);
        last.Number = "1";
        last.Tags = ["a"];

        Assert.Empty(Validate(chain));
    }

    private static T Nested<T>(int depth)
        where T : List<T>, new()
    {
        var outer = new T();

        for (var i = 0; i < depth; i++)
        {
            var next = new T { outer };
            outer = next;
        }

        return outer;
    }

    private static Maps NestedMaps(int depth)
    {
        var outer = new Maps();

        for (var i = 0; i < depth; i++)
        {
            outer = new Maps { ["k"] = [outer] };
        }

        return outer;
    }

    private static Link Last(Link chain)
    {
        while (chain.Next is not null)
        {
            chain = chain.Next;
        }

        return chain;
    }

    private static List<ModelError> Validate(object model)
    {
        var errors = new List<ModelError>();
        ModelValidation.Validate(model, string.Empty, Services, errors);

        return errors;
    }

    /// <summary>
    /// A chain of links whose last, at the given depth, is missing its required number.
    /// </summary>
    private static Link Chain(int depth)
    {
        var link = new Link();

        for (var i = 0; i < depth; i++)
        {
            link = new Link { Number = "1", Next = link };
        }

        return link;
    }

    /// <summary>
    /// A type with that many required string members, none of them set.
    /// </summary>
    private static Type ManyRequiredMembers(int count)
    {
        var module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("ModelValidationTests.Dynamic"), AssemblyBuilderAccess.Run)
            .DefineDynamicModule("ModelValidationTests.Dynamic");
        var type = module.DefineType("ManyRequiredMembers", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed);
        var required = new CustomAttributeBuilder(typeof(RequiredAttribute).GetConstructor(Type.EmptyTypes)!, []);

        for (var i = 0; i < count; i++)
        {
            var field = type.DefineField($"_member{i}", typeof(string), FieldAttributes.Private);
            var property = type.DefineProperty($"Member{i}", PropertyAttributes.None, typeof(string), null);
            var getter = type.DefineMethod(
                $"get_Member{i}",
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                typeof(string),
                Type.EmptyTypes);
            var il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ret);
            property.SetGetMethod(getter);
            property.SetCustomAttribute(required);
        }

        return type.CreateType();
    }

    private sealed class Order
    {
        public Dictionary<string, Line> Lines { get; } = [];

        public Dictionary<string, string> Tags { get; } = [];
    }

    private sealed class Line
    {
        [Required]
        public string? Number { get; set; }
    }

    private sealed record Widget : Resource
    {
        public string? Name { get; init; }
    }

    private sealed record Vault : Resource
    {
        [Encrypted(MaxLength = 50)]
        public string? Short { get; init; }

        [Encrypted(MaxLength = 1000)]
        public string? Long { get; init; }
    }

    private sealed record Unbounded : Resource
    {
        public string? Name { get; init; }

        [Encrypted]
        public string? Secret { get; init; }
    }

    private sealed class Batch
    {
        public List<Refusing> Items { get; init; } = [];
    }

    private sealed class Refusing : IValidatableObject
    {
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
            [new ValidationResult("Refused.")];
    }

    private sealed class Link
    {
        [Required]
        public string? Number { get; set; }

        public Link? Next { get; set; }

        public List<string>? Tags { get; set; }

        public List<Link>? Links { get; set; }
    }

    private sealed class Nest
    {
        public Lists? Lists { get; set; }

        public Maps? Maps { get; set; }
    }

    private sealed class Lists : List<Lists>;

    private sealed class Maps : Dictionary<string, List<Maps>>;
}
