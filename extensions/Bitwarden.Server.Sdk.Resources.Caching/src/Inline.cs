using System.Linq.Expressions;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// A lambda's body with its parameter replaced, so conditions compose into one predicate a provider translates.
/// </summary>
internal static class Inline
{
    public static Expression Body(LambdaExpression lambda, ParameterExpression row) =>
        new Replace(lambda.Parameters[0], row).Visit(lambda.Body);

    private sealed class Replace(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
