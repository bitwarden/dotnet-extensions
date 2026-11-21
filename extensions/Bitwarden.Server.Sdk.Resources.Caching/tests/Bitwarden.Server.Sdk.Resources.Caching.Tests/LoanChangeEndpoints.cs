using Microsoft.EntityFrameworkCore;

namespace Bitwarden.Server.Sdk.Resources.Caching.Tests;

/// <summary>
/// Changes to a loan through the service's own endpoints, as a domain service makes them.
/// </summary>
public static class LoanChangeEndpoints
{
    [StandardAction("return")]
    public static async Task Return(string id, ShelfContext database, CancellationToken cancellationToken)
    {
        (await database.Loans.SingleAsync(loan => loan.Id == Guid.Parse(id), cancellationToken)).Returned = true;
        await database.SaveChangesAsync(cancellationToken);
    }

    [StandardAction("return-in-transaction")]
    public static async Task ReturnInTransaction(string id, ShelfContext database, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        (await database.Loans.SingleAsync(loan => loan.Id == Guid.Parse(id), cancellationToken)).Returned = true;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    [StandardAction("renew")]
    public static async Task Renew(string id, ShelfContext database, CancellationToken cancellationToken)
    {
        (await database.Loans.SingleAsync(loan => loan.Id == Guid.Parse(id), cancellationToken)).Returned = false;
        await database.SaveChangesAsync(cancellationToken);
    }

    [StandardDelete]
    public static async Task DeleteLoan(string id, ShelfContext database, CancellationToken cancellationToken)
    {
        database.Loans.Remove(await database.Loans.SingleAsync(loan => loan.Id == Guid.Parse(id), cancellationToken));
        await database.SaveChangesAsync(cancellationToken);
    }
}
