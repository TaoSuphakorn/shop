using System.Net;
using System.Net.Http.Json;
using System.Text;
using Shop.Cart;

namespace Shop.IntegrationTests;

[Collection("shop")]
public class CartTests(ShopFactory f) : IAsyncLifetime
{
    public Task InitializeAsync() => f.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<CartDto> Cart(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CartDto>())!;
    }

    [Fact]
    public async Task Add_increase_decrease_remove_clear_keep_reserved_in_sync()   // spec 15 case 5
    {
        var c = f.CreateGuest();
        var cart = await Cart(await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 2)));
        Assert.Equal(5980m, cart.Total);
        Assert.Equal(23, cart.Items[0].Available);
        Assert.NotNull(cart.ExpiresAt);
        Assert.Equal((25, 2), await f.StockAsync(1));

        await Cart(await c.PutAsJsonAsync("/api/cart/items/1", new SetQuantityRequest(5)));
        Assert.Equal((25, 5), await f.StockAsync(1));
        await Cart(await c.PutAsJsonAsync("/api/cart/items/1", new SetQuantityRequest(1)));
        Assert.Equal((25, 1), await f.StockAsync(1));

        await Cart(await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(3, 4)));
        cart = await Cart(await c.DeleteAsync("/api/cart/items/1"));
        Assert.Equal(3, Assert.Single(cart.Items).ProductId);
        Assert.Equal((25, 0), await f.StockAsync(1));
        await f.AssertInvariantsAsync();

        cart = await Cart(await c.DeleteAsync("/api/cart"));
        Assert.Empty(cart.Items);
        Assert.Null(cart.ExpiresAt);
        Assert.Equal((40, 0), await f.StockAsync(3));
        await f.AssertInvariantsAsync();
    }

    [Fact]
    public async Task Adding_the_same_product_twice_sums_the_quantity()
    {
        var c = f.CreateGuest();
        await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 2));
        var cart = await Cart(await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 3)));
        Assert.Equal(5, Assert.Single(cart.Items).Quantity);
        Assert.Equal((25, 5), await f.StockAsync(1));
    }

    [Fact]
    public async Task Reservation_expires_fifteen_minutes_after_the_last_change()
    {
        var cart = await Cart(await f.CreateGuest().PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 1)));
        Assert.InRange(cart.ExpiresAt!.Value, DateTime.UtcNow.AddMinutes(14), DateTime.UtcNow.AddMinutes(16));
    }

    [Fact]
    public async Task Carts_are_private_to_each_guest()
    {
        await f.CreateGuest().PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 1));
        Assert.Empty((await f.CreateGuest().GetFromJsonAsync<CartDto>("/api/cart"))!.Items);
    }

    [Fact]
    public async Task Fifty_guests_adding_a_five_unit_product_five_succeed()   // spec 15 case 1
    {
        var rs = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ =>
            f.CreateGuest().PostKeyedAsync("/api/cart/items", new AddItemRequest(10, 1))));
        Assert.Equal(5, rs.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(45, rs.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal((5, 5), await f.StockAsync(10));
        await f.AssertInvariantsAsync();
    }

    [Fact]
    public async Task Out_of_stock_returns_409_with_available_and_leaves_cart_unchanged()
    {
        var c = f.CreateGuest();
        await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(4, 2));
        var r = await c.PutAsJsonAsync("/api/cart/items/4", new SetQuantityRequest(4));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("out_of_stock", await r.CodeAsync());
        Assert.Contains("\"available\":1", await r.Content.ReadAsStringAsync());
        Assert.Equal(2, (await c.GetFromJsonAsync<CartDto>("/api/cart"))!.Items[0].Quantity);
        Assert.Equal((3, 2), await f.StockAsync(4));
    }

    [Fact]
    public async Task Sold_out_product_cannot_be_added() =>
        Assert.Equal("out_of_stock", await (await f.CreateGuest().PostKeyedAsync("/api/cart/items", new AddItemRequest(11, 1))).CodeAsync());

    [Theory]   // spec 15 case 9
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100)]
    public async Task Invalid_quantity_is_400(int quantity)
    {
        var c = f.CreateGuest();
        Assert.Equal("validation_failed", await (await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, quantity))).CodeAsync());
        Assert.Equal("validation_failed", await (await c.PutAsJsonAsync("/api/cart/items/1", new SetQuantityRequest(quantity))).CodeAsync());
        Assert.Equal((25, 0), await f.StockAsync(1));
    }

    [Fact]
    public async Task Quantity_above_99_after_adding_is_400()
    {
        var c = f.CreateGuest();
        await f.ExecAsync($"UPDATE inventory.Stock SET OnHand = 500 WHERE ProductId = 9");
        await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(9, 60));
        var r = await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(9, 40));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal((500, 60), await f.StockAsync(9));
    }

    [Fact]
    public async Task Non_numeric_quantity_is_400() =>
        Assert.Equal(HttpStatusCode.BadRequest, (await f.CreateGuest().PutAsync("/api/cart/items/1",
            new StringContent("""{"quantity":"abc"}""", Encoding.UTF8, "application/json"))).StatusCode);

    [Fact]
    public async Task Unknown_product_is_404()   // Review Focus 5
    {
        var r = await f.CreateGuest().PostKeyedAsync("/api/cart/items", new AddItemRequest(999, 1));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("not_found", await r.CodeAsync());
    }

    [Fact]
    public async Task Removing_a_product_that_is_not_in_the_cart_does_nothing()
    {
        var c = f.CreateGuest();
        await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 1));
        var cart = await Cart(await c.DeleteAsync("/api/cart/items/3"));
        Assert.Equal(1, Assert.Single(cart.Items).ProductId);
    }

    [Fact]
    public async Task Sweeper_releases_expired_cart()   // spec 15 case 6
    {
        await f.CreateGuest().PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 3));
        var keep = f.CreateGuest();
        await keep.PostKeyedAsync("/api/cart/items", new AddItemRequest(3, 1));
        await f.ExecAsync($"UPDATE cart.Carts SET ExpiresAt = DATEADD(minute, -1, SYSUTCDATETIME()) WHERE GuestId IN (SELECT GuestId FROM cart.CartItems WHERE ProductId = 1)");

        Assert.True(await f.Service<CartSweeper>().RunOnceAsync(default));
        Assert.False(await f.Service<CartSweeper>().RunOnceAsync(default));
        Assert.Equal((25, 0), await f.StockAsync(1));
        Assert.Equal((40, 1), await f.StockAsync(3));   // the live cart is untouched
        Assert.Equal(1, await f.ScalarAsync<int>($"SELECT COUNT(*) FROM cart.Carts"));
    }

    [Fact]
    public async Task Adding_to_an_expired_unswept_cart_releases_the_old_items_first()   // Review Focus 2
    {
        var c = f.CreateGuest();
        await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 3));
        await f.ExecAsync($"UPDATE cart.Carts SET ExpiresAt = DATEADD(minute, -1, SYSUTCDATETIME())");
        Assert.Empty((await c.GetFromJsonAsync<CartDto>("/api/cart"))!.Items);

        var cart = await Cart(await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(3, 1)));
        Assert.Equal(3, Assert.Single(cart.Items).ProductId);
        Assert.Equal((25, 0), await f.StockAsync(1));
        await f.AssertInvariantsAsync();
    }

    [Fact]
    public async Task Concurrent_updates_from_one_guest_keep_reserved_equal_to_cart()   // Review Focus 1
    {
        var c = f.CreateGuest();
        await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 1));
        var rs = await Task.WhenAll(Enumerable.Range(1, 20).Select(q => c.PutAsJsonAsync("/api/cart/items/1", new SetQuantityRequest(q))));
        Assert.All(rs, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var quantity = (await c.GetFromJsonAsync<CartDto>("/api/cart"))!.Items[0].Quantity;
        Assert.Equal((25, quantity), await f.StockAsync(1));
    }

    [Fact]
    public async Task First_two_requests_of_a_new_guest_in_parallel_both_succeed()
    {
        var c = f.CreateGuest();
        await c.GetAsync("/api/cart");   // obtain the cookie
        var rs = await Task.WhenAll(
            c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 1)),
            c.PostKeyedAsync("/api/cart/items", new AddItemRequest(3, 1)));
        Assert.All(rs, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(2, (await c.GetFromJsonAsync<CartDto>("/api/cart"))!.Items.Count);
    }
}
