using Microsoft.EntityFrameworkCore;
using Shop.Shared;

namespace Shop.Inventory;

/// <summary>
/// Every stock change is one conditional UPDATE whose affected-row count decides success (spec 7).
/// Never read a quantity into C# and write it back: two pods would both see "enough".
/// </summary>
public sealed class InventoryApi(InventoryDbContext db) : IInventoryApi
{
    public async Task ReserveAsync(int productId, int quantity, CancellationToken ct)
    {
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE inventory.Stock SET Reserved = Reserved + {quantity}
            WHERE ProductId = {productId} AND OnHand - Reserved >= {quantity}
            """, ct);
        if (rows == 1) return;

        var available = (await GetAvailabilityAsync([productId], ct)).GetValueOrDefault(productId);
        throw new ApiException(409, "out_of_stock", extensions: new Dictionary<string, object?> { ["available"] = available });
    }

    public async Task ReleaseAsync(IReadOnlyCollection<StockLine> lines, CancellationToken ct)
    {
        foreach (var line in Ordered(lines))
            Require(await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE inventory.Stock SET Reserved = Reserved - {line.Quantity}
                WHERE ProductId = {line.ProductId} AND Reserved >= {line.Quantity}
                """, ct), "release", line);
    }

    public async Task CommitAsync(IReadOnlyCollection<StockLine> lines, CancellationToken ct)
    {
        foreach (var line in Ordered(lines))
            Require(await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE inventory.Stock SET OnHand = OnHand - {line.Quantity}, Reserved = Reserved - {line.Quantity}
                WHERE ProductId = {line.ProductId} AND Reserved >= {line.Quantity}
                """, ct), "commit", line);
    }

    public async Task RestockAsync(IReadOnlyCollection<StockLine> lines, CancellationToken ct)
    {
        foreach (var line in Ordered(lines))
            Require(await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE inventory.Stock SET OnHand = OnHand + {line.Quantity} WHERE ProductId = {line.ProductId}
                """, ct), "restock", line);
    }

    public async Task<IReadOnlyDictionary<int, int>> GetAvailabilityAsync(IReadOnlyCollection<int>? productIds, CancellationToken ct)
    {
        var query = db.Stock.AsNoTracking();
        if (productIds is not null) query = query.Where(s => productIds.Contains(s.ProductId));
        return await query.ToDictionaryAsync(s => s.ProductId, s => s.OnHand - s.Reserved, ct);
    }

    // Always touch rows in ProductId order so two transactions cannot deadlock on each other.
    private static IEnumerable<StockLine> Ordered(IEnumerable<StockLine> lines) => lines.OrderBy(l => l.ProductId);

    private static void Require(int rows, string operation, StockLine line)
    {
        if (rows != 1)
            throw new InvalidOperationException($"Stock {operation} of {line.Quantity} for product {line.ProductId} matched no row.");
    }
}
