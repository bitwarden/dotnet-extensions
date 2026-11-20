using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// Every cacheable query the service registers, and the resource each declares.
/// </summary>
/// <remarks>
/// <para>
/// Found from the registrations themselves: a query registered by type (<c>TryAddScoped&lt;IGetGroupQuery,
/// GetGroupQuery&gt;()</c>) whose implementation is a <see cref="CacheableQuery{TEntity,TResource}"/> turns caching on
/// for its resource. Read once, as the container first needs it, from the service collection <c>AddOwnedCache</c> was
/// given, so a query registered after that call is found as well.
/// </para>
/// <para>
/// Each query's <c>Configure</c> runs once per type, on an instance whose constructor has not run: a declaration is
/// about the type, and can't read what an instance is given.
/// </para>
/// </remarks>
internal sealed class CacheableQueries
{
    private readonly Dictionary<Type, ICacheableResource> _byQuery = [];
    private readonly Dictionary<Type, ICacheableResource> _byService = [];
    private readonly Dictionary<MethodInfo, (ICacheableResource Resource, CacheableOperation Operation)> _operations = [];
    private readonly Lock _settling = new();

    public CacheableQueries(IServiceCollection services)
    {
        var resources = new List<ICacheableResource>();

        foreach (var registration in services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ImplementationType is not null))
        {
            var implementation = registration.ImplementationType!;

            if (QueryBase(implementation) is null)
            {
                continue;
            }

            if (!_byQuery.TryGetValue(implementation, out var resource))
            {
                resource = Declare(implementation);
                _byQuery[implementation] = resource;
                resources.Add(resource);
            }

            _byService[registration.ServiceType] = resource;
        }

        // A table may back several queries, each under a key of its own: a membership by identifier, and by its
        // person within its organization. Every change to a row invalidates each one's entry.
        foreach (var table in resources.GroupBy(resource => resource.Entity).Where(table => table.Count() > 1))
        {
            if (table.GroupBy(resource => resource.Template("service")).FirstOrDefault(key => key.Count() > 1) is { } twice)
            {
                throw new InvalidOperationException(
                    $"{string.Join(" and ", twice.Select(resource => resource.Query.Name))} are cacheable queries of "
                    + $"{table.Key.Name} under the same key ({twice.Key}). Each query of a table declares a key of its own.");
            }

            if (table.Select(resource => resource.VersionProperty).Distinct(StringComparer.Ordinal).Count() > 1
                || table.Select(resource => resource.ResourceType).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                throw new InvalidOperationException(
                    $"{string.Join(" and ", table.Select(resource => resource.Query.Name))} are cacheable queries of "
                    + $"{table.Key.Name} that disagree on its version or its resource. Queries of one table read one "
                    + "resource at one version.");
            }
        }

        Resources = resources;
    }

    /// <summary>
    /// Every declared resource.
    /// </summary>
    public IReadOnlyList<ICacheableResource> Resources { get; }

    /// <summary>
    /// The resource a query declares, by its implementation.
    /// </summary>
    /// <exception cref="InvalidOperationException">The query isn't registered by type, so nothing invalidates its entries.</exception>
    public ICacheableResource For(Type query) =>
        _byQuery.GetValueOrDefault(query) ?? throw new InvalidOperationException(
            $"{query.Name} is a cacheable query that isn't registered by its type (TryAddScoped<IQuery, {query.Name}>()), "
            + "so the service cache knows nothing of its resource and would never invalidate it.");

    /// <summary>
    /// The resource a handler's parameter of this type reads, or null when it is no cacheable query.
    /// </summary>
    public ICacheableResource? ReadBy(Type parameter) =>
        _byService.GetValueOrDefault(parameter) ?? _byQuery.GetValueOrDefault(parameter);

    /// <summary>
    /// Records what an operation reading a cacheable query demands, as the platform settled it.
    /// </summary>
    public void Settle(MethodInfo method, ICacheableResource resource, CacheableOperation operation)
    {
        lock (_settling)
        {
            _operations[method] = (resource, operation);
        }
    }

    /// <summary>
    /// The cacheable read a handler serves, once settled.
    /// </summary>
    public (ICacheableResource Resource, CacheableOperation Operation)? Operation(MethodInfo method)
    {
        lock (_settling)
        {
            return _operations.TryGetValue(method, out var settled) ? settled : null;
        }
    }

    /// <summary>
    /// The closed <c>CacheableQuery&lt;,&gt;</c> a type derives from, or null.
    /// </summary>
    internal static Type? QueryBase(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(CacheableQuery<,>))
            {
                return current;
            }
        }

        return null;
    }

    private static ICacheableResource Declare(Type implementation)
    {
        if (implementation.IsAbstract)
        {
            throw new InvalidOperationException($"{implementation.Name} is registered as a cacheable query, but is abstract.");
        }

        var query = (ICacheableQuery)RuntimeHelpers.GetUninitializedObject(implementation);
        return query.Declare();
    }
}
