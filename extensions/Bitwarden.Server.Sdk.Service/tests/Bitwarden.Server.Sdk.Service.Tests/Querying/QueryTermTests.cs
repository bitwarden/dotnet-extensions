using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A term built in code starts as a literal-free leaf with no operands, so code walking an
/// expression never meets a null list.
/// </summary>
public sealed class QueryTermTests
{
    [Fact]
    public void New_HasNoOperatorFieldValueOrTerms()
    {
        var actual = new QueryTerm();

        Assert.Null(actual.Operator);
        Assert.Null(actual.Field);
        Assert.Null(actual.Value);
        Assert.NotNull(actual.Terms);
        Assert.Empty(actual.Terms);
    }

    [Fact]
    public void Equality_IsByValueForTheSameTerms()
    {
        QueryTerm[] terms = [new() { Operator = QueryOperator.Var, Field = "name" }];

        Assert.Equal(
            new QueryTerm { Operator = QueryOperator.Not, Terms = terms },
            new QueryTerm { Operator = QueryOperator.Not, Terms = terms });
    }
}
