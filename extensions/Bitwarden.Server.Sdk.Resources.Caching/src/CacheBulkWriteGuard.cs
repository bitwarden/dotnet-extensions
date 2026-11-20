using System.Linq.Expressions;
using Bitwarden.Server.Sdk.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// Refuses <c>ExecuteUpdate</c> and <c>ExecuteDelete</c> on a table a cache entry is built from, but for an owner's
/// erasure.
/// </summary>
/// <remarks>
/// <para>
/// A write in bulk passes no row through the change tracker, so nothing sees which entries it makes stale or which
/// versions it makes dead, and readers of the cache would be served the rows it replaced until they expire. Such a
/// change is made by loading the rows and saving them, in batches where there are many.
/// </para>
/// <para>
/// An owner's erasure deletes in bulk, and tells the cache itself (<see cref="CacheErasureObserver"/>). Its statements
/// are told apart explicitly: each carries <see cref="OwnerErasure.Tag"/>, and is compiled while
/// <see cref="OwnerErasure.Erasing"/> says so. The check runs as Entity Framework compiles a statement, which it does
/// once per shape; the tag is part of the shape, so an erasure's statement and any other never share one.
/// </para>
/// <para>
/// Raw SQL can't be read reliably, so it isn't checked: it mustn't write to such a table.
/// </para>
/// <para>
/// One instance for every context: Entity Framework builds its internal services anew for each instance of a query
/// interceptor it is given. The service's declarations are read from the context's own application services.
/// </para>
/// </remarks>
internal sealed class CacheBulkWriteGuard : IQueryExpressionInterceptor
{
    public static readonly CacheBulkWriteGuard Instance = new();

    private CacheBulkWriteGuard()
    {
    }

    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        if (queryExpression is not MethodCallExpression { Method: { Name: "ExecuteUpdate" or "ExecuteDelete", IsGenericMethod: true } method }
            || eventData.Context is not { } context)
        {
            return queryExpression;
        }

        var table = method.GetGenericArguments()[0];
        var registry = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?
            .GetService(typeof(OwnedCacheRegistry)) as OwnedCacheRegistry;

        if (registry is null || !registry.BuiltFrom(context, table))
        {
            return queryExpression;
        }

        var tagged = Tagged(queryExpression);

        if (tagged && OwnerErasure.Erasing)
        {
            return queryExpression;
        }

        throw new InvalidOperationException(
            tagged
                ? $"An {method.Name} on {table.Name} carries the owner erasure's tag outside an owner's erasure. Only "
                    + "OwnerErasure.EraseAsync may delete in bulk from a table the service cache is built from."
                : $"{method.Name} on {table.Name} is refused: the service cache's entries are built from it, and a write in "
                    + "bulk passes no row through the change tracker, so they would never be invalidated. Load the rows and "
                    + "save them, in batches where there are many.");
    }

    private static bool Tagged(Expression query)
    {
        var finder = new TagFinder();
        finder.Visit(query);
        return finder.Found;
    }

    private sealed class TagFinder : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == nameof(EntityFrameworkQueryableExtensions.TagWith)
                && node.Arguments is [_, ConstantExpression { Value: OwnerErasure.Tag }])
            {
                Found = true;
            }

            return base.VisitMethodCall(node);
        }
    }
}
