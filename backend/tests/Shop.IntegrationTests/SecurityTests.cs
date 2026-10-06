using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shop.Cart;

namespace Shop.IntegrationTests;

[Collection("shop")]
public class SecurityTests(ShopFactory f) : IAsyncLifetime
{
    public Task InitializeAsync() => f.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static StringContent Keyed(string body, string contentType)
    {
        var content = new StringContent(body, Encoding.UTF8, contentType);
        content.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return content;
    }

    [Theory]   // spec 15 case 11
    [InlineData("/api/cart/items/1;DROP TABLE cart.Carts")]
    [InlineData("/api/cart/items/1' OR 1=1--")]
    [InlineData("/api/cart/items/1 UNION SELECT 1")]
    public async Task Sql_injection_in_path_is_rejected(string path)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await f.CreateGuest().DeleteAsync(path)).StatusCode);
        Assert.Equal(12, await f.ScalarAsync<int>($"SELECT COUNT(*) FROM inventory.Stock"));
        Assert.Equal(1, await f.ScalarAsync<int>($"SELECT COUNT(*) FROM sys.tables WHERE name = 'Carts'"));
    }

    [Fact]
    public async Task Sql_injection_in_body_and_key_is_rejected()
    {
        var c = f.CreateGuest();
        var injected = await c.PostAsync("/api/cart/items",
            Keyed("""{"productId":"1; DROP TABLE catalog.Products","quantity":1}""", "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, injected.StatusCode);
        Assert.Equal("idempotency_key_required",
            await (await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 1), key: "x' OR '1'='1' -- pad pad")).CodeAsync());
        Assert.Equal(12, await f.ScalarAsync<int>($"SELECT COUNT(*) FROM catalog.Products"));
    }

    [Theory]   // Review Focus 3
    [InlineData("{not json", "application/json")]
    [InlineData("", "application/json")]
    [InlineData("""{"productId":1,"quantity":1,"price":0}""", "application/json")]   // unknown field
    [InlineData("""{"productId":1,"quantity":1}""", "text/plain")]
    [InlineData("productId=1&quantity=1", "application/x-www-form-urlencoded")]
    public async Task Bad_bodies_are_400_validation_failed(string body, string contentType)
    {
        var r = await f.CreateGuest().PostAsync("/api/cart/items", Keyed(body, contentType));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("validation_failed", await r.CodeAsync());
        Assert.Equal((25, 0), await f.StockAsync(1));
    }

    [Fact]
    public async Task Body_over_4kb_is_rejected_with_413()
    {
        var body = "{\"productId\":1,\"quantity\":1}" + new string(' ', 5000);   // valid JSON, only too large
        var r = await f.CreateGuest().PostAsync("/api/cart/items", Keyed(body, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
        Assert.Equal((25, 0), await f.StockAsync(1));
    }

    [Fact]
    public async Task Checkout_is_rate_limited()
    {
        var c = f.WithWebHostBuilder(b => b.UseSetting("RateLimit:CheckoutPerMinute", "2"))
            .CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await c.PostKeyedAsync("/api/orders")).StatusCode);   // cart_empty, but counted
        var r = await c.PostKeyedAsync("/api/orders");
        Assert.Equal((HttpStatusCode)429, r.StatusCode);
        Assert.Equal("rate_limited", await r.CodeAsync());
    }

    [Fact]
    public async Task Cart_changes_are_rate_limited_but_reads_are_not()
    {
        var c = f.WithWebHostBuilder(b => b.UseSetting("RateLimit:CartPerMinute", "1"))
            .CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.OK, (await c.DeleteAsync("/api/cart")).StatusCode);
        Assert.Equal((HttpStatusCode)429, (await c.DeleteAsync("/api/cart")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/cart")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/products")).StatusCode);
    }

    [Fact]
    public async Task Responses_carry_nosniff_no_store_and_no_cors_headers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/cart") { Headers = { { "Origin", "https://evil.example" } } };
        var r = await f.CreateGuest().SendAsync(request);
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(r.Headers.CacheControl!.NoStore);
        Assert.False(r.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Preflight_from_another_origin_is_not_approved()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/cart/items")
        {
            Headers = { { "Origin", "https://evil.example" }, { "Access-Control-Request-Method", "POST" } },
        };
        Assert.False((await f.CreateGuest().SendAsync(request)).Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Server_errors_do_not_leak_internals()
    {
        await f.ExecAsync($"EXEC sp_rename 'inventory.Stock', 'StockRenamed'");
        try
        {
            var r = await f.CreateGuest().GetAsync("/api/products");
            var body = await r.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode);
            Assert.Equal("internal_error", await r.CodeAsync());
            Assert.DoesNotContain("Stock", body);
            Assert.DoesNotContain("   at ", body);
        }
        finally
        {
            await f.ExecAsync($"EXEC sp_rename 'inventory.StockRenamed', 'Stock'");
        }
    }

    [Fact]
    public void Source_has_no_banned_calls()   // spec 15: automatic code check
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(root.FullName, ".git"))) root = root.Parent!;

        var banned = new Regex(@"ExecuteSqlRaw|FromSqlRaw|dangerouslySetInnerHTML|\.innerHTML|\beval\(");
        var skipped = new Regex(@"[\\/](bin|obj|node_modules|\.next|tests|\.superpowers|\.git)[\\/]");
        var hits = Directory.EnumerateFiles(root.FullName, "*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".cs") || p.EndsWith(".ts") || p.EndsWith(".tsx"))
            .Where(p => !skipped.IsMatch(p) && banned.IsMatch(File.ReadAllText(p)))
            .ToList();
        Assert.Empty(hits);
    }
}
