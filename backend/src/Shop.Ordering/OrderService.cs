using Microsoft.EntityFrameworkCore;
using Shop.Shared;

namespace Shop.Ordering;

public record OrderItemDto(int ProductId, string Sku, string Name, decimal UnitPrice, int Quantity, decimal LineTotal);
public record OrderDto(Guid Id, string Status, decimal Total, DateTime CreatedAt, DateTime? CancelledAt, IReadOnlyList<OrderItemDto> Items);

/// <summary>Order rules that need no database, kept apart so they can be unit tested.</summary>
public static class OrderRules
{
    /// <summary>Prices and names are copied from the catalog now, so the order never changes when the catalog does.</summary>
    public static Order Create(Guid guestId, IReadOnlyCollection<StockLine> lines, IEnumerable<ProductInfo> products, DateTime now)
    {
        if (lines.Count == 0) throw new ApiException(400, "cart_empty", "the cart is empty or its reservation has expired");

        var byId = products.ToDictionary(p => p.Id);
        var order = new Order { Id = Guid.NewGuid(), GuestId = guestId, Status = Order.Paid, CreatedAt = now };
        order.Items = lines.OrderBy(l => l.ProductId).Select(l =>
        {
            var p = byId[l.ProductId];
            return new OrderItem
            {
                OrderId = order.Id, ProductId = p.Id, Sku = p.Sku, Name = p.Name,
                UnitPrice = p.Price, Quantity = l.Quantity, LineTotal = p.Price * l.Quantity,
            };
        }).ToList();
        order.Total = order.Items.Sum(i => i.LineTotal);
        return order;
    }

    public static OrderDto ToDto(Order order) => new(order.Id, order.Status, order.Total, order.CreatedAt, order.CancelledAt,
        order.Items.OrderBy(i => i.ProductId)
            .Select(i => new OrderItemDto(i.ProductId, i.Sku, i.Name, i.UnitPrice, i.Quantity, i.LineTotal)).ToList());
}

public sealed class OrderService(OrderingDbContext db, UnitOfWork unitOfWork, ICartApi cart, ICatalogApi catalog, IInventoryApi inventory)
{
    /// <summary>
    /// Checkout (spec 9.2), all in one transaction: take the cart, price it from the catalog,
    /// turn the reservations into a real stock decrement, and record the order. Payment is simulated.
    /// </summary>
    public Task<OrderDto> CheckoutAsync(Guid guestId, CancellationToken ct) =>
        unitOfWork.ExecuteAsync(async c =>
        {
            var lines = await cart.TakeItemsForCheckoutAsync(guestId, c);
            var products = await catalog.GetProductsAsync(lines.Select(l => l.ProductId).ToArray(), c);
            var order = OrderRules.Create(guestId, lines, products, DateTime.UtcNow);
            await inventory.CommitAsync(lines, c);
            db.Orders.Add(order);
            await db.SaveChangesAsync(c);
            return OrderRules.ToDto(order);
        }, ct);

    /// <summary>Someone else's order is reported exactly like a missing one, so ids cannot be probed.</summary>
    public async Task<OrderDto> GetAsync(Guid guestId, Guid orderId, CancellationToken ct) =>
        OrderRules.ToDto(await db.Orders.AsNoTracking().Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.Id == orderId && o.GuestId == guestId, ct)
            ?? throw new ApiException(404, "not_found", "order does not exist"));
}
