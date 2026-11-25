using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// The navigations a cached resource's read includes (<c>Including</c>), whose rows its entry is built from too.
/// </summary>
/// <remarks>
/// <para>
/// A change to an included row moves its parent's version on, in the database as the change commits
/// (<see cref="VersionBump"/>), so the parent's entry is invalidated as a change to the parent's own row would be.
/// Read from the shape the declaration gives, applied to the context's own query, so what is included is what the read
/// includes.
/// </para>
/// <para>
/// Only what a row's foreign key can name its parent by: a collection or a reference on the parent, one level deep,
/// whose foreign key, a property of the included row, points at the parent's cache key. A navigation to a row the
/// parent refers to (a change to it would move every parent referring to it), one through a join table, or an include of
/// an include, is refused as the host starts.
/// </para>
/// </remarks>
internal static class IncludedRows
{
    /// <summary>
    /// The navigations <paramref name="query"/>, the cached read's shape, includes from <paramref name="entity"/>.
    /// </summary>
    /// <param name="query">The read's shape, over the context's own query of the row.</param>
    /// <param name="entity">The row's entity type.</param>
    /// <param name="resource">The resource, for a refusal.</param>
    /// <param name="keyProperty">The property the row is cached by, which a foreign key must name it by.</param>
    /// <exception cref="InvalidOperationException">An include the cache can't invalidate.</exception>
    public static IReadOnlyList<INavigation> Of(IQueryable query, IEntityType entity, string resource, string keyProperty)
    {
        var finder = new Finder();
        finder.Visit(query.Expression);
        var navigations = new List<INavigation>();


        foreach (var call in finder.Includes)
        {
            if (call.Method.Name != nameof(EntityFrameworkQueryableExtensions.Include))
            {
                throw Refused(resource, call.ToString(), "an include of an include: a change to its rows can't name the cached row");
            }

            var name = call.Arguments[1] switch
            {
                ConstantExpression { Value: string path } when !path.Contains('.', StringComparison.Ordinal) => path,
                var argument when Unquote(argument) is LambdaExpression { Body: MemberExpression { Expression: var of } body } lambda
                    && of == lambda.Parameters[0] => body.Member.Name,
                var argument when Unquote(argument) is LambdaExpression lambda && Member(lambda) is { } member =>
                    throw Refused(resource, member, "a filtered include: the rows it leaves out are a rule a client can't repeat"),
                _ => throw Refused(resource, call.ToString(), "not a navigation of the cached row itself"),
            };

            var navigation = entity.FindNavigation(name)
                ?? throw Refused(resource, name, "not a navigation with a foreign key of its own (a many-to-many through a join table?)");

            if (navigation.IsOnDependent)
            {
                throw Refused(resource, name, "a row the cached row refers to: a change to it would move every row referring to it");
            }

            if (navigation.ForeignKey.PrincipalKey.Properties is not [{ } principal] || principal.Name != keyProperty)
            {
                throw Refused(resource, name, $"its foreign key doesn't name the cached row by its {keyProperty}");
            }

            if (navigation.ForeignKey.Properties[0].IsShadowProperty())
            {
                throw Refused(resource, name, "its foreign key is no property of the included row, which an erasure reads it by");
            }

            navigations.Add(navigation);
        }

        // Anything but includes would change which row is read, or what it includes, in a way a client can't repeat:
        // a filter, an order, a read past row-level security.
        if (finder.Others.FirstOrDefault() is { } other)
        {
            throw new InvalidOperationException(
                $"{resource}'s cached read shapes its query with {other.Method.Name}, which a client of the cache can't "
                + "repeat. A cached read may only include navigations (Include).");
        }

        return navigations;
    }

    private static InvalidOperationException Refused(string resource, string include, string why) =>
        new($"{resource}'s cached read includes {include}, which the service cache can't invalidate: {why}. Leave it out "
            + "of the cached read, or cache what it adds elsewhere.");

    private static Expression Unquote(Expression expression) =>
        expression is UnaryExpression { NodeType: ExpressionType.Quote } quote ? quote.Operand : expression;

    /// <summary>
    /// The cached row's own member the lambda reads: <c>row.Memberships</c>, filtered or not.
    /// </summary>
    private static string? Member(LambdaExpression lambda)
    {
        var member = new MemberFinder(lambda.Parameters[0]);
        member.Visit(lambda.Body);
        return member.Found;
    }

    private sealed class Finder : ExpressionVisitor
    {
        public List<MethodCallExpression> Includes { get; } = [];

        public List<MethodCallExpression> Others { get; } = [];

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
                && node.Method.Name is nameof(EntityFrameworkQueryableExtensions.Include) or nameof(EntityFrameworkQueryableExtensions.ThenInclude))
            {
                Includes.Add(node);
            }
            else if (node.Method.DeclaringType == typeof(RelationalQueryableExtensions)
                && node.Method.Name is nameof(RelationalQueryableExtensions.AsSplitQuery) or nameof(RelationalQueryableExtensions.AsSingleQuery))
            {
                // How the include is loaded, not what.
            }
            else
            {
                Others.Add(node);
                return node;
            }

            return base.VisitMethodCall(node);
        }
    }

    private sealed class MemberFinder(ParameterExpression row) : ExpressionVisitor
    {
        public string? Found { get; private set; }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression == row)
            {
                Found ??= node.Member.Name;
            }

            return base.VisitMember(node);
        }
    }
}
