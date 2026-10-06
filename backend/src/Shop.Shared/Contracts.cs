namespace Shop.Shared;

// The only way modules talk to each other synchronously. When a module is extracted into its own
// service, its interface here gets an HTTP implementation and callers do not change.

public record StockLine(int ProductId, int Quantity);

public record ProductInfo(int Id, string Sku, string Name, string Description, decimal Price, string ImageUrl);

public record OrderCancelled(Guid OrderId, IReadOnlyList<StockLine> Items);

public interface IInventoryApi
{
    /// <summary>Throws ApiException 409 out_of_stock (extension "available") when there is not enough.</summary>
    Task ReserveAsync(int productId, int quantity, CancellationToken ct);
    Task ReleaseAsync(IReadOnlyCollection<StockLine> lines, CancellationToken ct);
    Task CommitAsync(IReadOnlyCollection<StockLine> lines, CancellationToken ct);
    Task RestockAsync(IReadOnlyCollection<StockLine> lines, CancellationToken ct);
    /// <summary>Available = OnHand - Reserved. Pass null for every product.</summary>
    Task<IReadOnlyDictionary<int, int>> GetAvailabilityAsync(IReadOnlyCollection<int>? productIds, CancellationToken ct);
}

public interface ICatalogApi
{
    /// <summary>Pass null for every product.</summary>
    Task<IReadOnlyList<ProductInfo>> GetProductsAsync(IReadOnlyCollection<int>? ids, CancellationToken ct);
}

public interface ICartApi
{
    /// <summary>Returns the cart's lines and deletes the cart without releasing its reservations.</summary>
    Task<IReadOnlyList<StockLine>> TakeItemsForCheckoutAsync(Guid guestId, CancellationToken ct);
}
