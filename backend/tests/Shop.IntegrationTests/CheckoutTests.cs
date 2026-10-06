using System.Net;
using System.Net.Http.Json;
using Shop.Cart;
using Shop.Ordering;

namespace Shop.IntegrationTests;

[Collection("shop")]
public class CheckoutTests(ShopFactory f) : IAsyncLifetime
{
    public Task InitializeAsync() => f.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Checkout_shows_total_cuts_stock_and_empties_cart()
    {
        var c = f.CreateGuest();
        await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(4, 2));
        await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(9, 3));

        var r = await c.PostKeyedAsync("/api/orders");
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var o = (await r.Content.ReadFromJsonAsync<OrderDto>())!;
        Assert.Equal("Paid", o.Status);
        Assert.Equal(4950m, o.Total);   // 1890*2 + 390*3
        Assert.Null(o.CancelledAt);
        Assert.Equal([(4, "TNC-004", 1890m, 2, 3780m), (9, "TNC-009", 390m, 3, 1170m)],
            o.Items.Select(i => (i.ProductId, i.Sku, i.UnitPrice, i.Quantity, i.LineTotal)));

        Assert.Equal((1, 0), await f.StockAsync(4));
        Assert.Equal((97, 0), await f.StockAsync(9));
        Assert.Empty((await c.GetFromJsonAsync<CartDto>("/api/cart"))!.Items);
        Assert.Equal(await r.Content.ReadAsStringAsync(), await c.GetStringAsync($"/api/orders/{o.Id}"));
        await f.AssertInvariantsAsync();
    }

    [Fact]
    public async Task Ten_parallel_checkouts_with_one_key_create_one_order()   // spec 15 case 2
    {
        var c = f.CreateGuest();
        await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 2));
        var key = Guid.NewGuid().ToString();
        var rs = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => c.PostKeyedAsync("/api/orders", null, key)));
        Assert.All(rs, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Single((await Task.WhenAll(rs.Select(r => r.Content.ReadAsStringAsync()))).Distinct());
        Assert.Equal(1, await f.ScalarAsync<int>($"SELECT COUNT(*) FROM ordering.Orders"));
        Assert.Equal((23, 0), await f.StockAsync(1));
    }

    [Fact]
    public async Task Double_click_with_two_keys_creates_one_order_and_one_empty_cart_error()
    {
        var c = f.CreateGuest();
        await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 2));
        var rs = await Task.WhenAll(c.PostKeyedAsync("/api/orders"), c.PostKeyedAsync("/api/orders"));
        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.BadRequest], rs.Select(r => r.StatusCode).Order());
        Assert.Equal((23, 0), await f.StockAsync(1));
    }

    [Fact]
    public async Task Price_is_read_from_database_at_checkout()
    {
        var c = f.CreateGuest();
        await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(9, 1));
        await f.ExecAsync($"UPDATE catalog.Products SET Price = 500 WHERE Id = 9");
        Assert.Equal(500m, (await (await c.PostKeyedAsync("/api/orders")).Content.ReadFromJsonAsync<OrderDto>())!.Total);
    }

    [Fact]
    public async Task Order_keeps_its_price_when_the_catalog_changes_later()
    {
        var c = f.CreateGuest();
        await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(9, 2));
        var o = (await (await c.PostKeyedAsync("/api/orders")).Content.ReadFromJsonAsync<OrderDto>())!;
        await f.ExecAsync($"UPDATE catalog.Products SET Price = 9999, Name = {"changed"} WHERE Id = 9");
        var again = (await c.GetFromJsonAsync<OrderDto>($"/api/orders/{o.Id}"))!;
        Assert.Equal(780m, again.Total);
        Assert.Equal("สายชาร์จ USB-C 100W", again.Items[0].Name);
    }

    [Fact]
    public async Task Checkout_does_not_accept_a_request_body()
    {
        var c = f.CreateGuest();
        await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(9, 1));
        var o = (await (await c.PostKeyedAsync("/api/orders", new { total = 1 })).Content.ReadFromJsonAsync<OrderDto>())!;
        Assert.Equal(390m, o.Total);   // a client-sent total is never used
    }

    [Fact]
    public async Task Empty_cart_is_400_cart_empty()   // spec 15 case 10
    {
        var r = await f.CreateGuest().PostKeyedAsync("/api/orders");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("cart_empty", await r.CodeAsync());
    }

    [Fact]
    public async Task Checkout_requires_an_idempotency_key() =>
        Assert.Equal("idempotency_key_required", await (await f.CreateGuest().PostAsync("/api/orders", null)).CodeAsync());

    [Fact]
    public async Task Expired_unswept_cart_cannot_be_checked_out()   // Review Focus 2
    {
        var c = f.CreateGuest();
        await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 2));
        await f.ExecAsync($"UPDATE cart.Carts SET ExpiresAt = DATEADD(minute, -1, SYSUTCDATETIME())");
        Assert.Equal("cart_empty", await (await c.PostKeyedAsync("/api/orders")).CodeAsync());
        Assert.Equal((25, 2), await f.StockAsync(1));   // nothing was sold
        await f.Service<CartSweeper>().RunOnceAsync(default);
        Assert.Equal((25, 0), await f.StockAsync(1));
    }

    [Fact]
    public async Task Another_guests_order_is_404()   // spec 15 case 7
    {
        var a = f.CreateGuest();
        await a.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 1));
        var o = (await (await a.PostKeyedAsync("/api/orders")).Content.ReadFromJsonAsync<OrderDto>())!;
        var r = await f.CreateGuest().GetAsync($"/api/orders/{o.Id}");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("not_found", await r.CodeAsync());
    }

    [Theory]   // Review Focus 5
    [InlineData("/api/orders/1;DROP TABLE x")]
    [InlineData("/api/orders/not-a-guid")]
    [InlineData("/api/orders/00000000-0000-0000-0000-000000000000")]
    public async Task Unknown_or_malformed_order_id_is_404(string path) =>
        Assert.Equal(HttpStatusCode.NotFound, (await f.CreateGuest().GetAsync(path)).StatusCode);
}
