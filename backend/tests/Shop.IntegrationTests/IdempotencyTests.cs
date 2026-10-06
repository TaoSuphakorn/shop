using System.Net;
using System.Net.Http.Json;
using Shop.Cart;
using Shop.Shared;

namespace Shop.IntegrationTests;

[Collection("shop")]
public class IdempotencyTests(ShopFactory f) : IAsyncLifetime
{
    public Task InitializeAsync() => f.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Missing_or_malformed_key_is_400()
    {
        var c = f.CreateGuest();
        var missing = await c.PostAsJsonAsync("/api/cart/items", new AddItemRequest(1, 1));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("idempotency_key_required", await missing.CodeAsync());
        Assert.Equal("idempotency_key_required",
            await (await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 1), key: "short")).CodeAsync());
        Assert.Equal((25, 0), await f.StockAsync(1));
    }

    [Fact]
    public async Task Replay_returns_same_response_and_reserves_once()
    {
        var c = f.CreateGuest();
        var key = Guid.NewGuid().ToString();
        var a = await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 2), key);
        var b = await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 2), key);
        Assert.Equal(HttpStatusCode.OK, a.StatusCode);
        Assert.Equal(a.StatusCode, b.StatusCode);
        Assert.Equal(await a.Content.ReadAsStringAsync(), await b.Content.ReadAsStringAsync());
        Assert.Equal((25, 2), await f.StockAsync(1));
    }

    [Fact]
    public async Task Ten_parallel_requests_with_one_key_reserve_once()
    {
        var c = f.CreateGuest();
        await c.GetAsync("/api/cart");
        var key = Guid.NewGuid().ToString();
        var rs = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 1), key)));
        Assert.All(rs, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal((25, 1), await f.StockAsync(1));
    }

    [Fact]
    public async Task Same_key_with_different_body_is_422()   // spec 15 case 3
    {
        var c = f.CreateGuest();
        var key = Guid.NewGuid().ToString();
        await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 1), key);
        var r = await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 2), key);
        Assert.Equal((HttpStatusCode)422, r.StatusCode);
        Assert.Equal("idempotency_key_reuse", await r.CodeAsync());
        Assert.Equal((25, 1), await f.StockAsync(1));
    }

    [Fact]
    public async Task Failed_request_does_not_consume_the_key()
    {
        var c = f.CreateGuest();
        var key = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(11, 1), key)).StatusCode);
        await f.ExecAsync($"UPDATE inventory.Stock SET OnHand = 1 WHERE ProductId = 11");
        Assert.Equal(HttpStatusCode.OK, (await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(11, 1), key)).StatusCode);
    }

    [Fact]
    public async Task Two_guests_can_use_the_same_key()
    {
        var key = Guid.NewGuid().ToString();
        await f.CreateGuest().PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 1), key);
        await f.CreateGuest().PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 1), key);
        Assert.Equal((25, 2), await f.StockAsync(1));
    }

    [Fact]
    public async Task Cleanup_deletes_keys_older_than_a_day_and_keeps_recent_ones()
    {
        var c = f.CreateGuest();
        await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 1));
        await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 1));
        await f.ExecAsync($"UPDATE TOP (1) shared.IdempotencyKeys SET CreatedAt = DATEADD(hour, -25, SYSUTCDATETIME())");
        Assert.Equal(1, await f.Service<IdempotencyCleanup>().RunOnceAsync(default));
        Assert.Equal(1, await f.ScalarAsync<int>($"SELECT COUNT(*) FROM shared.IdempotencyKeys"));
    }
}
