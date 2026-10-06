using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Shop.Shared;

namespace Shop.Cart;

public sealed class CartService(CartDbContext db, UnitOfWork unitOfWork, IInventoryApi inventory, ICatalogApi catalog,
    IConfiguration configuration) : ICartApi
{
    private const int CartBusy = 51000;
    private readonly TimeSpan _reservation = TimeSpan.FromMinutes(configuration.GetValue("Cart:ReservationMinutes", 15));

    /// <summary>Read-only view. A cart past its expiry reads as empty even before the sweeper removes it.</summary>
    public async Task<CartDto> GetAsync(Guid guestId, CancellationToken ct)
    {
        var cart = await db.Carts.AsNoTracking().SingleOrDefaultAsync(c => c.GuestId == guestId, ct);
        if (cart is null || cart.ExpiresAt <= DateTime.UtcNow) return await ToDtoAsync([], default, ct);
        var items = await db.CartItems.AsNoTracking().Where(i => i.GuestId == guestId).ToListAsync(ct);
        return await ToDtoAsync(items, cart.ExpiresAt, ct);
    }

    public Task<CartDto> AddAsync(Guid guestId, int productId, int quantity, CancellationToken ct) =>
        ChangeAsync(guestId, productId, current => current + quantity, quantity, ct);

    public Task<CartDto> SetQuantityAsync(Guid guestId, int productId, int quantity, CancellationToken ct) =>
        ChangeAsync(guestId, productId, _ => quantity, quantity, ct);

    public Task<CartDto> RemoveAsync(Guid guestId, int productId, CancellationToken ct) =>
        MutateAsync(guestId, async items =>
        {
            if (items.SingleOrDefault(i => i.ProductId == productId) is not { } item) return;
            await inventory.ReleaseAsync([new StockLine(productId, item.Quantity)], ct);
            items.Remove(item);
        }, ct);

    public Task<CartDto> ClearAsync(Guid guestId, CancellationToken ct) =>
        MutateAsync(guestId, async items =>
        {
            await inventory.ReleaseAsync(Lines(items), ct);
            items.Clear();
        }, ct);

    public Task<IReadOnlyList<StockLine>> TakeItemsForCheckoutAsync(Guid guestId, CancellationToken ct) =>
        unitOfWork.ExecuteAsync<IReadOnlyList<StockLine>>(async c =>
        {
            var (cart, items) = await LockCartAsync(guestId, c);
            db.Carts.Remove(cart);   // items cascade; their reservations stay for the caller to commit
            await db.SaveChangesAsync(c);
            return Lines(items);
        }, ct);

    private async Task<CartDto> ChangeAsync(Guid guestId, int productId, Func<int, int> newQuantity, int requested, CancellationToken ct)
    {
        CartRules.ValidateQuantity(requested);
        if (productId <= 0 || (await catalog.GetProductsAsync([productId], ct)).Count == 0)
            throw new ApiException(404, "not_found", "product does not exist");

        return await MutateAsync(guestId, async items =>
        {
            var item = items.SingleOrDefault(i => i.ProductId == productId);
            var current = item?.Quantity ?? 0;
            var target = newQuantity(current);
            CartRules.ValidateQuantity(target);
            if (item is null) CartRules.ValidateLineCount(items.Count + 1);

            // Stock moves by the difference only; Reserve throws 409 out_of_stock and rolls everything back.
            if (target > current) await inventory.ReserveAsync(productId, target - current, ct);
            if (target < current) await inventory.ReleaseAsync([new StockLine(productId, current - target)], ct);

            if (item is null) items.Add(new CartItem { GuestId = guestId, ProductId = productId, Quantity = target });
            else item.Quantity = target;
        }, ct);
    }

    private Task<CartDto> MutateAsync(Guid guestId, Func<List<CartItem>, Task> change, CancellationToken ct) =>
        unitOfWork.ExecuteAsync(async c =>
        {
            var (cart, items) = await LockCartAsync(guestId, c);
            var before = items.ToList();
            await change(items);

            db.CartItems.RemoveRange(before.Except(items));
            db.CartItems.AddRange(items.Except(before));
            if (items.Count == 0) db.Carts.Remove(cart);
            else cart.ExpiresAt = DateTime.UtcNow + _reservation;   // every change restarts the 15 minutes
            await db.SaveChangesAsync(c);
            return await ToDtoAsync(items, cart.ExpiresAt, c);
        }, ct);

    /// <summary>
    /// Serializes everything that touches one guest's cart, across pods.
    /// The app lock covers the "row does not exist yet" case; UPDLOCK on the row keeps the sweeper out.
    /// An expired cart is emptied here, so stale reservations are never sold or carried into a new cart.
    /// </summary>
    private async Task<(ShoppingCart Cart, List<CartItem> Items)> LockCartAsync(Guid guestId, CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sp_getapplock @Resource = {"cart:" + guestId.ToString("N")}, @LockMode = 'Exclusive',
                     @LockOwner = 'Transaction', @LockTimeout = 5000;
                IF @result < 0 THROW 51000, 'cart is busy', 1;
                """, ct);
        }
        catch (Exception ex) when (UnitOfWork.SqlError(ex)?.Number == CartBusy)
        {
            throw new ApiException(409, "request_in_progress");
        }

        var cart = await db.Carts
            .FromSqlInterpolated($"SELECT * FROM cart.Carts WITH (UPDLOCK, ROWLOCK) WHERE GuestId = {guestId}")
            .SingleOrDefaultAsync(ct);
        if (cart is null)
        {
            cart = new ShoppingCart { GuestId = guestId, ExpiresAt = DateTime.UtcNow + _reservation };
            db.Carts.Add(cart);
            await db.SaveChangesAsync(ct);
            return (cart, []);
        }

        var items = await db.CartItems.Where(i => i.GuestId == guestId).ToListAsync(ct);
        if (cart.ExpiresAt <= DateTime.UtcNow && items.Count > 0)
        {
            await inventory.ReleaseAsync(Lines(items), ct);
            db.CartItems.RemoveRange(items);
            await db.SaveChangesAsync(ct);
            items = [];
        }
        return (cart, items);
    }

    private async Task<CartDto> ToDtoAsync(List<CartItem> items, DateTime expiresAt, CancellationToken ct)
    {
        if (items.Count == 0) return CartRules.ToDto([], [], new Dictionary<int, int>(), expiresAt);
        var ids = items.Select(i => i.ProductId).ToArray();
        return CartRules.ToDto(Lines(items), await catalog.GetProductsAsync(ids, ct),
            await inventory.GetAvailabilityAsync(ids, ct), expiresAt);
    }

    private static List<StockLine> Lines(IEnumerable<CartItem> items) =>
        items.Select(i => new StockLine(i.ProductId, i.Quantity)).ToList();
}
