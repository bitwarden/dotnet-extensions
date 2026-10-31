using System.ComponentModel.DataAnnotations;
using Bitwarden.Server.Sdk.Service.Endpoints;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A dictionary's values are validated as a controller validates them, each keyed by the entry's
/// own key, and the key reaches the error's pointer as it was sent.
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
        Assert.Equal(pointer, RequestRefusal.Pointer(key));
    }

    private static List<ModelError> Validate(object model)
    {
        var errors = new List<ModelError>();
        ModelValidation.Validate(model, string.Empty, Services, errors);

        return errors;
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
}
