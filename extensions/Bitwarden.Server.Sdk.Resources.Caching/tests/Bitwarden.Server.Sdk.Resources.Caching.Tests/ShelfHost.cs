using System.Text;
using Bitwarden.Server.Sdk.Authorization;
using Bitwarden.Server.Sdk.Context;
using Bitwarden.Server.Sdk.Data;
using Bitwarden.Server.Sdk.Errors;
using Bitwarden.Server.Sdk.Resources.Querying;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Bitwarden.Server.Sdk.Resources.Caching.Tests;

/// <summary>
/// A domain service owning a cache of books, hosted in memory over SQLite.
/// </summary>
public sealed class ShelfHost : IAsyncDisposable
{
    public const string ServiceName = "shelf-service";

    public const string ReadScope = "shelves.read";

    /// <summary>
    /// The setting giving the database's command timeout in seconds; the provider's own unless set.
    /// </summary>
    public const string CommandTimeoutKey = "Shelf:CommandTimeout";

    private const string Issuer = "https://identity.shelf.test";

    private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes("shelf-host-test-signing-key-signs-nothing-real"));

    private readonly WebApplication _app;
    private readonly SqliteConnection _connection;

    private ShelfHost(WebApplication app, SqliteConnection connection)
    {
        _app = app;
        _connection = connection;
    }

    public IServiceProvider Services => _app.Services;

    public HttpClient Client() => _app.GetTestClient();

    /// <summary>
    /// Starts the service. With no <paramref name="redis"/>, there is no cache.
    /// </summary>
    /// <param name="redis">The cache's connection, or null for none.</param>
    /// <param name="declare">The service's registrations: its cache's declaration.</param>
    /// <param name="environmentName">The host's environment.</param>
    /// <param name="settings">Any other configuration.</param>
    /// <param name="databaseFile">
    /// A SQLite file in write-ahead mode, so a second connection reads what is committed while a transaction is open;
    /// null for a database in memory.
    /// </param>
    public static async Task<ShelfHost> StartAsync(
        string? redis,
        Action<IServiceCollection> declare,
        string environmentName = "Testing",
        IReadOnlyDictionary<string, string?>? settings = null,
        string? databaseFile = null)
    {
        var connection = new SqliteConnection(databaseFile is null ? "DataSource=:memory:" : $"DataSource={databaseFile}");
        connection.Open();

        if (databaseFile is not null)
        {
            using var wal = connection.CreateCommand();
            wal.CommandText = "PRAGMA journal_mode=WAL;";
            wal.ExecuteNonQuery();
        }

        var configuration = new Dictionary<string, string?>(settings ?? new Dictionary<string, string?>())
        {
            [OwnedCacheEntry.ConnectionKey] = redis,
        };

        var app = new ShelfService(connection, configuration, declare, environmentName).Build();

        using (var scope = app.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ShelfContext>().Database.EnsureCreated();
        }

        await app.StartAsync(TestContext.Current.CancellationToken);

        return new ShelfHost(app, connection);
    }

    /// <summary>
    /// A service token: internal, no acting user.
    /// </summary>
    public static string Token(params string[] scopes) => TokenFor("internal.some-service", scopes);

    /// <summary>
    /// A service token for this client, with these scopes.
    /// </summary>
    public static string TokenFor(string client, params string[] scopes) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            // What a service's token for this callee names, and so what it accepts.
            Audience = ServiceName,
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(5),
            TokenType = "at+jwt",
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object> { ["client_id"] = client, ["scope"] = scopes },
        });

    public async Task<HttpResponseMessage> GetAsync(
        string path,
        Guid? organization,
        Guid? user = null,
        string client = "internal.some-service",
        bool asserted = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new("Bearer", TokenFor(client, ReadScope));

        if (organization is { } named)
        {
            request.Headers.Add(BitwardenHeaders.Organization, named.ToString());
        }

        if (user is { } acting)
        {
            request.Headers.Add(BitwardenHeaders.User, acting.ToString());
        }

        // The context, signed as the token exchange signs it for a call to this service.
        request.Headers.Add(BitwardenHeaders.Delegation, SignedContextToken(organization, user, client, asserted));

        return await Client().SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A context token for a call to this service from the service <see cref="Token"/> names.
    /// </summary>
    public static string SignedContextToken(Guid? organization, Guid? user, string client = "internal.some-service", bool asserted = false)
    {
        var claims = new Dictionary<string, object>
        {
            [ContextToken.ClientClaim] = client,
            [ContextToken.ModeClaim] = asserted ? ContextToken.Asserted : ContextToken.Delegated,
            [ContextToken.ContextClaim] = """{"v":1}""",
        };

        if (organization is { } named)
        {
            claims[ContextToken.OrganizationClaim] = named.ToString();
        }

        if (user is { } acting)
        {
            claims[ContextToken.UserClaim] = acting.ToString();
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = ServiceName,
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(5),
            TokenType = ContextToken.Type,
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256),
            Claims = claims,
        });
    }

    /// <summary>
    /// Runs <paramref name="work"/> with the services of a scope acting for <paramref name="organization"/>: a
    /// query's own read, with no endpoint in front of it.
    /// </summary>
    public async Task InServiceScopeAsync(Guid organization, Func<IServiceProvider, Task> work)
    {
        using var scope = Services.CreateScope();
        using var context = ContextScope.Begin(new StatedContext { OrganizationId = organization });
        await work(scope.ServiceProvider);
    }

    /// <summary>
    /// Runs <paramref name="work"/> in a scope acting for <paramref name="organization"/>.
    /// </summary>
    public async Task InScopeAsync(Guid? organization, Func<ShelfContext, Task> work)
    {
        using var scope = Services.CreateScope();
        using var context = organization is { } named ? ContextScope.Begin(new StatedContext { OrganizationId = named }) : null;
        await work(scope.ServiceProvider.GetRequiredService<ShelfContext>());
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync(CancellationToken.None);
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class ShelfService(
        SqliteConnection connection,
        IReadOnlyDictionary<string, string?> configuration,
        Action<IServiceCollection> declare,
        string environmentName)
        : Bitwarden.Server.Sdk.Service.Service(ServiceName, "Shelf Service", [])
    {
        protected override AuthorizationDefaults Authorization => new()
        {
            Organization = ContextRequirement.Required,
            ReadScope = ReadScope,
            WriteScope = "shelves.write",
        };

        protected override WebApplicationBuilder CreateBuilder()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = environmentName,
                ApplicationName = typeof(ShelfService).Assembly.GetName().Name,
            });

            builder.WebHost.UseTestServer();

            // Outside Development a service needs a secret to derive its operations' write keys with
            // (Idempotency:Secret), read as the host starts: a fixed one, 32 bytes of 0x07, never the development secret.
            builder.Configuration["Idempotency:Secret"] = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray());

            foreach (var (key, value) in configuration)
            {
                builder.Configuration[key] = value;
            }

            return builder;
        }

        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddOpenApi();
            // A test's own command timeout, in seconds, lets it run with a commit allowance of seconds.
            var timeout = configuration.TryGetValue(CommandTimeoutKey, out var seconds) && seconds is not null
                ? int.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture)
                : (int?)null;
            services.AddDbContext<ShelfContext>(options => options.UseSqlite(connection, sqlite => sqlite.CommandTimeout(timeout)));

            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.MetadataAddress = null!;
                options.RequireHttpsMetadata = false;
                options.Configuration = new OpenIdConnectConfiguration();
                options.TokenValidationParameters.ValidIssuer = Issuer;
                options.TokenValidationParameters.IssuerSigningKey = Key;
                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                options.TokenValidationParameters.ValidateLifetime = true;
            });

            declare(services);
        }

        protected override void MapEndpoints(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints)
        {
            // A cacheable read only where the test registers its query, as the handler takes it.
            var registered = endpoints.ServiceProvider.GetRequiredService<IServiceProviderIsService>();

            if (registered.IsService(typeof(IGetBookQuery)))
            {
                var books = endpoints.MapGroup("api/v1/books").WithStandards();
                books.MapGet("{id}", BooksEndpoints.GetBook);

                // A read the platform must refuse to cache, where the test asks for one.
                switch (endpoints.ServiceProvider.GetService<BookRefusal>()?.Case)
                {
                    case "filtered":
                        Microsoft.AspNetCore.Http.EndpointFilterExtensions.AddEndpointFilter(books.MapGet("{id}/filtered", BooksEndpoints.GetBookFiltered), (context, next) => next(context));
                        break;
                    case "with-more":
                        books.MapGet("{id}/with-more", BooksEndpoints.GetBookTakingMore);
                        break;
                    case "by-policy":
                        books.MapGet("{id}/by-policy", BooksEndpoints.GetBookByPolicy);
                        break;
                    case "any-scope":
                        books.MapGet("{id}/any-scope", BooksEndpoints.GetBookAnyScope);
                        break;
                    case "anonymous":
                        books.MapGet("{id}/anonymous", BooksEndpoints.GetBookAnonymously);
                        break;
                    case "self-authorized":
                        books.MapGet("{id}/self-authorized", BooksEndpoints.GetBookSelfAuthorized);
                        break;
                }
            }

            if (registered.IsService(typeof(IGetQuotaQuery)))
            {
                endpoints.MapGroup("api/v1/quotas").WithStandards().MapGet("{organization}", QuotasEndpoints.GetQuota);
            }

            var loans = endpoints.MapGroup("api/v1/loans").WithStandards();

            if (registered.IsService(typeof(IGetLoanQuery)))
            {
                loans.MapGet("{id}", LoansEndpoints.GetLoan);
            }

            if (registered.IsService(typeof(IGetBorrowersLoanQuery)))
            {
                loans.MapGet("current", LoansEndpoints.GetCurrentLoan);
            }

            loans.MapPost("{id}/actions/return", LoanChangeEndpoints.Return);
            loans.MapPost("{id}/actions/return-in-transaction", LoanChangeEndpoints.ReturnInTransaction);
            loans.MapPost("{id}/actions/renew", LoanChangeEndpoints.Renew);
            loans.MapDelete("{id}", LoanChangeEndpoints.DeleteLoan);

            if (registered.IsService(typeof(IGetGuardedQuery)))
            {
                endpoints.MapGroup("api/v1/guarded").WithStandards().MapGet("{id}", GuardedEndpoints.GetGuarded);
            }

            if (registered.IsService(typeof(IGetReaderQuery)))
            {
                endpoints.MapGroup("api/v1/readers").WithStandards().MapGet("{id}", ReadersEndpoints.GetReader);
            }

            if (registered.IsService(typeof(IGetShelfQuery)))
            {
                endpoints.MapGroup("api/v1/shelves").WithStandards().MapGet("{id}", ShelvesEndpoints.GetShelf);
            }
        }
    }
}

[OwnedBy(Owner.Organization)]
public sealed class BookEntity
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public string Title { get; set; } = null!;

    public string? Shelfmark { get; set; }

    /// <summary>
    /// Protected Data: an encrypted string the service stores and never reads.
    /// </summary>
    public string? Inscription { get; set; }

    public int Version { get; set; }
}

[OwnedBy(Owner.Organization)]
public sealed class QuotaEntity
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public int Limit { get; set; }

    public int Version { get; set; }
}

[OwnedBy(Owner.Organization)]
public sealed class LoanEntity
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid? Borrower { get; set; }

    public bool Returned { get; set; }

    /// <summary>
    /// What is lent: the order a borrower's loans are read in.
    /// </summary>
    public string Title { get; set; } = "";

    public int Version { get; set; }
}

/// <summary>
/// A reader's card, whose writes are last-writer-wins: versioned for the cache by a column that is no concurrency token.
/// </summary>
[OwnedBy(Owner.Organization)]
public sealed class ReaderEntity
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public string Name { get; set; } = null!;

    public int CacheVersion { get; set; }
}

/// <summary>
/// A shelf, read with the books placed on it: its cached entry is built from its placements too.
/// </summary>
[OwnedBy(Owner.Organization)]
public sealed class ShelfEntity
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public string Name { get; set; } = null!;

    public int Version { get; set; }

    public ICollection<PlacementEntity> Placements { get; set; } = [];
}

/// <summary>
/// A book's place on a shelf: no resource of its own, included in its shelf's read.
/// </summary>
[OwnedBy(Owner.Organization)]
public sealed class PlacementEntity
{
    public Guid Id { get; set; }

    public Guid ShelfId { get; set; }

    public Guid OrganizationId { get; set; }

    public string Title { get; set; } = null!;
}

/// <summary>
/// A library branch: nothing cached is built from it, but the database deletes or changes rows that are when it goes.
/// </summary>
public sealed class BranchEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;
}

/// <summary>
/// A copy of a book held by a branch, which the database deletes with its branch.
/// </summary>
[OwnedBy(Owner.Organization)]
public sealed class CopyEntity
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid BranchId { get; set; }

    public string Title { get; set; } = null!;

    public int Version { get; set; }
}

/// <summary>
/// A notice a branch posted, which the database keeps without its branch when the branch goes.
/// </summary>
[OwnedBy(Owner.Organization)]
public sealed class NoticeEntity
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid? BranchId { get; set; }

    public int Version { get; set; }
}

/// <summary>
/// A branch's stamp, which only the change tracker deletes with its branch: the database refuses the branch's delete
/// while one it didn't load remains.
/// </summary>
[OwnedBy(Owner.Organization)]
public sealed class StampEntity
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid BranchId { get; set; }

    public int Version { get; set; }
}

/// <summary>
/// A borrower barred from borrowing: a table nothing cached is built from.
/// </summary>
public sealed class SuspensionEntity
{
    public Guid Id { get; set; }

    public Guid Borrower { get; set; }
}

/// <summary>
/// A row the service narrows by a query filter of its own: archived rows are never read.
/// </summary>
public sealed class ArchiveEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public bool Archived { get; set; }

    public int Version { get; set; }
}

public sealed record Archive : Resource
{
    public string Name { get; init; } = null!;
}

/// <summary>
/// A label, which an organization may have put up: erased with it, though no row-level filter restricts it.
/// </summary>
[ErasedWith(Owner.Organization, Checked = false)]
public sealed class LabelEntity
{
    public Guid Id { get; set; }

    public Guid? OrganizationId { get; set; }

    public string Text { get; set; } = null!;
}

public sealed record Book : Resource
{
    public string Title { get; init; } = null!;

    /// <summary>
    /// Not cached: worked out for each caller.
    /// </summary>
    public string? Shelfmark { get; init; }

    [Encrypted(MaxLength = 1000)]
    public string? Inscription { get; init; }
}

public sealed record Quota : Resource
{
    public Guid Organization { get; init; }

    public int Limit { get; init; }

    /// <summary>
    /// Whom to tell when it runs out: personal data, never cached across owners.
    /// </summary>
    [PersonalData]
    public string? Contact { get; init; }
}

public sealed record Loan : Resource
{
    public Guid Organization { get; init; }

    public Guid? Borrower { get; init; }

    public bool Returned { get; init; }

    public string? Title { get; init; }
}

public sealed record Label : Resource
{
    public string Text { get; init; } = null!;
}

public sealed record Reader : Resource
{
    public string Name { get; init; } = null!;
}

public sealed record Shelf : Resource
{
    public string Name { get; init; } = null!;

    public IReadOnlyList<string> Titles { get; init; } = [];
}

public sealed record Copy : Resource
{
    public string Title { get; init; } = null!;
}

public sealed record Notice : Resource
{
    public Guid? Branch { get; init; }
}

public sealed record Stamp : Resource
{
    public Guid Branch { get; init; }
}

public sealed record Guarded : Resource
{
    public string Note { get; init; } = null!;
}

internal static class ShelfMapping
{
    public static Book ToResource(BookEntity entity) => new()
    {
        Id = entity.Id.ToString(),
        Title = entity.Title,
        Shelfmark = entity.Shelfmark,
        Inscription = entity.Inscription,
        Version = entity.Version.ToString(),
    };

    public static Loan ToResource(LoanEntity entity) => new()
    {
        Id = entity.Id.ToString(),
        Organization = entity.OrganizationId,
        Borrower = entity.Borrower,
        Returned = entity.Returned,
        Title = entity.Title,
        Version = entity.Version.ToString(),
    };

    public static Reader ToResource(ReaderEntity entity) => new()
    {
        Id = entity.Id.ToString(),
        Name = entity.Name,
        Version = entity.CacheVersion.ToString(),
    };

    public static Copy ToResource(CopyEntity entity) => new()
    {
        Id = entity.Id.ToString(),
        Title = entity.Title,
        Version = entity.Version.ToString(),
    };

    public static Notice ToResource(NoticeEntity entity) => new()
    {
        Id = entity.Id.ToString(),
        Branch = entity.BranchId,
        Version = entity.Version.ToString(),
    };

    public static Stamp ToResource(StampEntity entity) => new()
    {
        Id = entity.Id.ToString(),
        Branch = entity.BranchId,
        Version = entity.Version.ToString(),
    };

    public static Shelf ToResource(ShelfEntity entity) => new()
    {
        Id = entity.Id.ToString(),
        Name = entity.Name,
        Titles = [.. entity.Placements.Select(placement => placement.Title).Order(StringComparer.Ordinal)],
        Version = entity.Version.ToString(),
    };

    public static Quota ToResource(QuotaEntity entity) => new()
    {
        Id = entity.Id.ToString(),
        Organization = entity.OrganizationId,
        Limit = entity.Limit,
        Version = entity.Version.ToString(),
    };
}

public sealed class ShelfContext(DbContextOptions<ShelfContext> options, IRowOwnership ownership)
    : DbContext(options), IRestrictedContext
{
    public DbSet<BookEntity> Books => Set<BookEntity>();

    public DbSet<QuotaEntity> Quotas => Set<QuotaEntity>();

    public DbSet<LabelEntity> Labels => Set<LabelEntity>();

    public DbSet<LoanEntity> Loans => Set<LoanEntity>();

    public DbSet<SuspensionEntity> Suspensions => Set<SuspensionEntity>();

    public DbSet<ReaderEntity> Readers => Set<ReaderEntity>();

    public DbSet<ShelfEntity> Shelves => Set<ShelfEntity>();

    public DbSet<PlacementEntity> Placements => Set<PlacementEntity>();

    public DbSet<ArchiveEntity> Archives => Set<ArchiveEntity>();

    public DbSet<BranchEntity> Branches => Set<BranchEntity>();

    public DbSet<CopyEntity> Copies => Set<CopyEntity>();

    public DbSet<NoticeEntity> Notices => Set<NoticeEntity>();

    public DbSet<StampEntity> Stamps => Set<StampEntity>();

    public Guid CurrentOrganization => ownership.RequireOrganization();

    public Guid CurrentUser => ownership.RequireUser();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<QuotaEntity>().HasIndex(quota => quota.OrganizationId).IsUnique();
        modelBuilder.Entity<ShelfEntity>().HasMany(shelf => shelf.Placements).WithOne().HasForeignKey(placement => placement.ShelfId);
        modelBuilder.Entity<ArchiveEntity>().HasQueryFilter(archive => !archive.Archived);

        // A branch's copies go with it in the database, its notices lose it, and its stamps hold it up: three delete
        // behaviours for the cascade check.
        modelBuilder.Entity<CopyEntity>().HasOne<BranchEntity>().WithMany().HasForeignKey(copy => copy.BranchId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<NoticeEntity>().HasOne<BranchEntity>().WithMany().HasForeignKey(notice => notice.BranchId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<StampEntity>().HasOne<BranchEntity>().WithMany().HasForeignKey(stamp => stamp.BranchId).OnDelete(DeleteBehavior.ClientCascade);
        RowLevelSecurity.Apply(modelBuilder, this);
    }
}

public static class BooksEndpoints
{
    [StandardReadOne]
    public static Task<ResourceDocument<Book>> GetBook(
        string id, IGetBookQuery query, Fields<Book> fields = default, CancellationToken cancellationToken = default)
        => query.ReadAsync(id, fields, cancellationToken);

    [StandardReadOne("{id}/filtered")]
    public static Task<ResourceDocument<Book>> GetBookFiltered(string id, IGetBookQuery query, CancellationToken cancellationToken = default)
        => query.ReadAsync(id, default, cancellationToken);

    [StandardReadOne("{id}/with-more")]
    public static Task<ResourceDocument<Book>> GetBookTakingMore(
        string id, IGetBookQuery query, ShelfContext database, CancellationToken cancellationToken = default)
        => query.ReadAsync(id, default, cancellationToken);

    [StandardReadOne("{id}/by-policy")]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = "librarians")]
    public static Task<ResourceDocument<Book>> GetBookByPolicy(string id, IGetBookQuery query, CancellationToken cancellationToken = default)
        => query.ReadAsync(id, default, cancellationToken);

    [StandardReadOne("{id}/any-scope")]
    [AnyScope(ShelfHost.ReadScope, "shelves.other")]
    public static Task<ResourceDocument<Book>> GetBookAnyScope(string id, IGetBookQuery query, CancellationToken cancellationToken = default)
        => query.ReadAsync(id, default, cancellationToken);

    [StandardReadOne("{id}/anonymous")]
    [Authorization(Anonymous = true)]
    public static Task<ResourceDocument<Book>> GetBookAnonymously(string id, IGetBookQuery query, CancellationToken cancellationToken = default)
        => query.ReadAsync(id, default, cancellationToken);

    [StandardReadOne("{id}/self-authorized")]
    [SelfAuthorized]
    public static Task<ResourceDocument<Book>> GetBookSelfAuthorized(string id, IGetBookQuery query, CancellationToken cancellationToken = default)
        => query.ReadAsync(id, default, cancellationToken);
}

/// <summary>
/// Which read of a book a test expects the platform to refuse to cache.
/// </summary>
public sealed record BookRefusal(string Case);

/// <summary>
/// A check of the handler's own, which a client of the cache can't repeat.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SelfAuthorizedAttribute : Attribute, IAuthorizesOperation
{
    public ValueTask AuthorizeAsync(Microsoft.AspNetCore.Http.HttpContext context) => ValueTask.CompletedTask;
}

public static class QuotasEndpoints
{
    [StandardReadOne("{organization}")]
    public static async Task<ResourceDocument<Quota>> GetQuota(
        Guid organization, IGetQuotaQuery query, Fields<Quota> fields = default, CancellationToken cancellationToken = default)
        => await query.ReadByKeyAsync(organization, fields, cancellationToken) ?? throw new NotFoundException();
}

public static class LoansEndpoints
{
    /// <summary>
    /// The person in context's loan in the organization in context.
    /// </summary>
    [StandardReadOne("current")]
    [Authorization(User = ContextRequirement.Required)]
    [AcceptsAssertedContext("A lending job acts about a borrower.", "some-service")]
    public static async Task<ResourceDocument<Loan>> GetCurrentLoan(
        IGetBorrowersLoanQuery query, Fields<Loan> fields = default, CancellationToken cancellationToken = default)
        => await query.ReadCurrentAsync(fields, cancellationToken) ?? throw new NotFoundException();

    [StandardReadOne]
    public static Task<ResourceDocument<Loan>> GetLoan(
        string id, IGetLoanQuery query, Fields<Loan> fields = default, CancellationToken cancellationToken = default)
        => query.ReadAsync(id, fields, cancellationToken);
}

public static class ReadersEndpoints
{
    [StandardReadOne]
    public static Task<ResourceDocument<Reader>> GetReader(
        string id, IGetReaderQuery query, Fields<Reader> fields = default, CancellationToken cancellationToken = default)
        => query.ReadAsync(id, fields, cancellationToken);
}

public static class ShelvesEndpoints
{
    [StandardReadOne]
    public static Task<ResourceDocument<Shelf>> GetShelf(
        string id, IGetShelfQuery query, Fields<Shelf> fields = default, CancellationToken cancellationToken = default)
        => query.ReadAsync(id, fields, cancellationToken);
}

public static class GuardedEndpoints
{
    [StandardReadOne]
    [Authorization(OrganizationRole = OrganizationRole.Owner)]
    public static Task<ResourceDocument<Guarded>> GetGuarded(string id, IGetGuardedQuery query, CancellationToken cancellationToken = default)
        => query.ReadAsync(id, default, cancellationToken);
}
