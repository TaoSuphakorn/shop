using Shop.Shared;

namespace Shop.Cart;

public record CartItemDto(int ProductId, string Sku, string Name, string ImageUrl, decimal UnitPrice, int Quantity, decimal LineTotal, int Available);
public record CartDto(IReadOnlyList<CartItemDto> Items, decimal Total, DateTime? ExpiresAt);
public record AddItemRequest(int ProductId, int Quantity);
public record SetQuantityRequest(int Quantity);

/// <summary>Cart rules that need no database, kept apart so they can be unit tested.</summary>
public static class CartRules
{
    public const int MaxQuantity = 99, MaxLines = 50;

    public static void ValidateQuantity(int quantity)
    {
        if (quantity is < 1 or > MaxQuantity)
            throw new ApiException(400, "validation_failed", $"quantity must be between 1 and {MaxQuantity}");
    }

    public static void ValidateLineCount(int lines)
    {
        if (lines > MaxLines)
            throw new ApiException(400, "validation_failed", $"a cart holds at most {MaxLines} different products");
    }

    /// <summary>Prices always come from the catalog; nothing the client sent is used.</summary>
    public static CartDto ToDto(IEnumerable<StockLine> lines, IEnumerable<ProductInfo> products,
        IReadOnlyDictionary<int, int> availability, DateTime expiresAt)
    {
        var byId = products.ToDictionary(p => p.Id);
        var items = lines.OrderBy(l => l.ProductId).Select(l =>
        {
            var p = byId[l.ProductId];
            return new CartItemDto(p.Id, p.Sku, p.Name, p.ImageUrl, p.Price, l.Quantity, p.Price * l.Quantity,
                availability.GetValueOrDefault(p.Id));
        }).ToList();
        return new CartDto(items, items.Sum(i => i.LineTotal), items.Count == 0 ? null : expiresAt);
    }
}
