using Microsoft.EntityFrameworkCore;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// A cached read whose entry is a page source: its order must end in a unique column, which the model shows.
/// </summary>
internal interface IUniquelyOrdered
{
    /// <summary>
    /// Refuses an order that doesn't end in a unique property of the table.
    /// </summary>
    /// <param name="context">The service's database, whose model has the table's keys and indexes.</param>
    /// <exception cref="InvalidOperationException">The order's last key is no unique property.</exception>
    void RequireUniqueOrder(DbContext context);
}
