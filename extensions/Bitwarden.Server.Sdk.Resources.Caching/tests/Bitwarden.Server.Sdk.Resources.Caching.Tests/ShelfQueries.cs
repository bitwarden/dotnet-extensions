using Bitwarden.Server.Sdk.Data;
using Bitwarden.Server.Sdk.Resources.Querying;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bitwarden.Server.Sdk.Resources.Caching.Tests;

public interface IGetBookQuery
{
    Task<ResourceDocument<Book>> ReadAsync(string id, Fields<Book> fields = default, CancellationToken cancellationToken = default);
}

/// <summary>
/// A book, its shelfmark worked out for each caller.
/// </summary>
public sealed class GetBookQuery(CacheableQueryServices services) : CacheableQuery<BookEntity, Book>(services), IGetBookQuery
{
    protected override void Configure(CacheableQueryBuilder<BookEntity, Book> query) => query
        .Map(ShelfMapping.ToResource)
        .PerCaller("shelfmark");

    protected override ValueTask<Book> CompleteAsync(BookEntity entity, Book resource, Fields<Book> fields, CancellationToken cancellationToken) =>
        ValueTask.FromResult(fields.Includes("shelfmark") ? resource with { Shelfmark = "A-1" } : resource);
}

public interface IGetQuotaQuery
{
    Task<ResourceDocument<Quota>?> ReadByKeyAsync(Guid organization, Fields<Quota> fields = default, CancellationToken cancellationToken = default);
}

/// <summary>
/// An organization's quota, by the organization, read across owners: its contact is never cached.
/// </summary>
public sealed class GetQuotaQuery(CacheableQueryServices services) : CacheableQuery<QuotaEntity, Quota>(services), IGetQuotaQuery
{
    protected override void Configure(CacheableQueryBuilder<QuotaEntity, Quota> query) => query
        .KeyedBy(entity => entity.OrganizationId, "organization")
        .AcrossOwners("a management read, narrowed by the organization it names")
        .Map(ShelfMapping.ToResource)
        .PerCaller("contact");
}

/// <summary>
/// An organization's quota, by the organization, read within the organization the request names.
/// </summary>
public sealed class OwnQuotaQuery(CacheableQueryServices services) : CacheableQuery<QuotaEntity, Quota>(services), IGetQuotaQuery
{
    protected override void Configure(CacheableQueryBuilder<QuotaEntity, Quota> query) => query
        .KeyedBy(entity => entity.OrganizationId, "organization")
        .Map(ShelfMapping.ToResource)
        .PerCaller("contact");
}

/// <summary>
/// Personal data cached across owners: refused.
/// </summary>
public sealed class QuotaWithContactQuery(CacheableQueryServices services) : CacheableQuery<QuotaEntity, Quota>(services), IGetQuotaQuery
{
    protected override void Configure(CacheableQueryBuilder<QuotaEntity, Quota> query) => query
        .KeyedBy(entity => entity.OrganizationId, "organization")
        .AcrossOwners("a management read")
        .Map(ShelfMapping.ToResource);
}

public interface IGetLoanQuery
{
    Task<ResourceDocument<Loan>> ReadAsync(string id, Fields<Loan> fields = default, CancellationToken cancellationToken = default);
}

public sealed class GetLoanQuery(CacheableQueryServices services) : CacheableQuery<LoanEntity, Loan>(services), IGetLoanQuery
{
    protected override void Configure(CacheableQueryBuilder<LoanEntity, Loan> query) => query.Map(ShelfMapping.ToResource);
}

/// <summary>
/// A loan nobody grants or refuses anything by.
/// </summary>
public sealed class ProceedingLoanQuery(CacheableQueryServices services) : CacheableQuery<LoanEntity, Loan>(services), IGetLoanQuery
{
    protected override void Configure(CacheableQueryBuilder<LoanEntity, Loan> query) => query
        .Map(ShelfMapping.ToResource)
        .WhenCacheUnreachable(CacheWrites.Proceed);
}

/// <summary>
/// A loan whose entries live a second: its tombstones too short for the fill limit and the commit allowance.
/// </summary>
public sealed class BriefLoanQuery(CacheableQueryServices services) : CacheableQuery<LoanEntity, Loan>(services), IGetLoanQuery
{
    protected override void Configure(CacheableQueryBuilder<LoanEntity, Loan> query) => query
        .Map(ShelfMapping.ToResource)
        .Lifetime(TimeSpan.FromSeconds(1));
}

public interface IGetBorrowersLoanQuery
{
    Task<ResourceDocument<Loan>?> ReadCurrentAsync(Fields<Loan> fields = default, CancellationToken cancellationToken = default);

    Task<ResourceDocument<Loan>?> ReadOwnedAsync(Guid owner, Guid key, Fields<Loan> fields = default, CancellationToken cancellationToken = default);
}

/// <summary>
/// The person in context's loan in the organization in context: a second query of the loans' table, keyed by the
/// borrower within the organization, and not unique.
/// </summary>
public sealed class GetBorrowersLoanQuery(CacheableQueryServices services) : CacheableQuery<LoanEntity, Loan>(services), IGetBorrowersLoanQuery
{
    protected override void Configure(CacheableQueryBuilder<LoanEntity, Loan> query) => query
        .KeyedBy(entity => entity.Borrower, "borrower")
        .WithinOwner()
        .NotUnique(rows => rows.OrderBy(row => row.Title).ThenBy(row => row.Id))
        .CurrentFrom(Owner.User)
        .Map(ShelfMapping.ToResource);
}

/// <summary>
/// A second query of the loans by identifier: under the same key as the first, refused.
/// </summary>
public sealed class LoanByIdentifierAgainQuery(CacheableQueryServices services) : CacheableQuery<LoanEntity, Loan>(services), IGetBorrowersLoanQuery
{
    protected override void Configure(CacheableQueryBuilder<LoanEntity, Loan> query) => query.Map(ShelfMapping.ToResource);
}

public interface IGetReaderQuery
{
    Task<ResourceDocument<Reader>> ReadAsync(string id, Fields<Reader> fields = default, CancellationToken cancellationToken = default);
}

public sealed class GetReaderQuery(CacheableQueryServices services) : CacheableQuery<ReaderEntity, Reader>(services), IGetReaderQuery
{
    protected override void Configure(CacheableQueryBuilder<ReaderEntity, Reader> query) => query
        .VersionedBy(nameof(ReaderEntity.CacheVersion))
        .Map(ShelfMapping.ToResource);
}

public interface IGetShelfQuery
{
    Task<ResourceDocument<Shelf>> ReadAsync(string id, Fields<Shelf> fields = default, CancellationToken cancellationToken = default);
}

public sealed class GetShelfQuery(CacheableQueryServices services) : CacheableQuery<ShelfEntity, Shelf>(services), IGetShelfQuery
{
    protected override void Configure(CacheableQueryBuilder<ShelfEntity, Shelf> query) => query
        .Including(rows => rows.Include(row => row.Placements))
        .Map(ShelfMapping.ToResource);
}

/// <summary>
/// An include of an include: refused.
/// </summary>
public sealed class DeepShelfQuery(CacheableQueryServices services) : CacheableQuery<ShelfEntity, Shelf>(services), IGetShelfQuery
{
    protected override void Configure(CacheableQueryBuilder<ShelfEntity, Shelf> query) => query
        .Including(rows => rows.Include("Placements.Shelf"))
        .Map(ShelfMapping.ToResource);
}

/// <summary>
/// A filtered include: refused.
/// </summary>
public sealed class FilteredShelfQuery(CacheableQueryServices services) : CacheableQuery<ShelfEntity, Shelf>(services), IGetShelfQuery
{
    protected override void Configure(CacheableQueryBuilder<ShelfEntity, Shelf> query) => query
        .Including(rows => rows.Include(row => row.Placements.Where(placement => placement.Title != "hidden")))
        .Map(ShelfMapping.ToResource);
}

/// <summary>
/// A read narrowed by a rule of its own: refused.
/// </summary>
public sealed class NarrowedShelfQuery(CacheableQueryServices services) : CacheableQuery<ShelfEntity, Shelf>(services), IGetShelfQuery
{
    protected override void Configure(CacheableQueryBuilder<ShelfEntity, Shelf> query) => query
        .Including(rows => rows.Where(row => row.Name != "hidden"))
        .Map(ShelfMapping.ToResource);
}

public interface IGetGuardedQuery
{
    Task<ResourceDocument<Guarded>> ReadAsync(string id, Fields<Guarded> fields = default, CancellationToken cancellationToken = default);
}

public sealed class GetGuardedQuery(CacheableQueryServices services) : CacheableQuery<BookEntity, Guarded>(services), IGetGuardedQuery
{
    protected override void Configure(CacheableQueryBuilder<BookEntity, Guarded> query) => query
        .Map(entity => new Guarded { Id = entity.Id.ToString(), Note = entity.Title });
}

public interface IGetLabelQuery
{
    Task<ResourceDocument<Label>> ReadAsync(string id, Fields<Label> fields = default, CancellationToken cancellationToken = default);
}

/// <summary>
/// A row with no version: refused.
/// </summary>
public sealed class GetLabelQuery(CacheableQueryServices services) : CacheableQuery<LabelEntity, Label>(services), IGetLabelQuery
{
    protected override void Configure(CacheableQueryBuilder<LabelEntity, Label> query) => query
        .Unowned()
        .Map(entity => new Label { Id = entity.Id.ToString(), Text = entity.Text });
}

/// <summary>
/// Declares an attribute per caller the resource doesn't have: refused.
/// </summary>
public sealed class MisnamedBookQuery(CacheableQueryServices services) : CacheableQuery<BookEntity, Book>(services), IGetBookQuery
{
    protected override void Configure(CacheableQueryBuilder<BookEntity, Book> query) => query
        .Map(ShelfMapping.ToResource)
        .PerCaller("author");
}

public interface IGetArchiveQuery
{
    Task<ResourceDocument<Archive>> ReadAsync(string id, Fields<Archive> fields = default, CancellationToken cancellationToken = default);
}

/// <summary>
/// A row narrowed by the service's own query filter: refused.
/// </summary>
public sealed class GetArchiveQuery(CacheableQueryServices services) : CacheableQuery<ArchiveEntity, Archive>(services), IGetArchiveQuery
{
    protected override void Configure(CacheableQueryBuilder<ArchiveEntity, Archive> query) => query
        .Unowned()
        .Map(entity => new Archive { Id = entity.Id.ToString(), Name = entity.Name, Version = entity.Version.ToString() });
}

public static class ShelfQueries
{
    /// <summary>
    /// Registers a cacheable query as a service registers any query.
    /// </summary>
    public static IServiceCollection AddQuery<TService, TQuery>(this IServiceCollection services)
        where TService : class
        where TQuery : class, TService
    {
        services.TryAddScoped<TService, TQuery>();
        return services;
    }

    /// <summary>
    /// The declaration of a query, as the platform finds it.
    /// </summary>
    internal static ICacheableResource Declare<TService, TQuery>()
        where TService : class
        where TQuery : class, TService =>
        new CacheableQueries(new ServiceCollection().AddQuery<TService, TQuery>()).Resources.Single();
}
