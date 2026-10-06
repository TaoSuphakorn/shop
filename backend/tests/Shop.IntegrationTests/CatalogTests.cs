using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Shop.Catalog;

namespace Shop.IntegrationTests;

[Collection("shop")]
public class CatalogTests(ShopFactory f) : IAsyncLifetime
{
    public Task InitializeAsync() => f.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Lists_twelve_products_with_availability()
    {
        var r = await f.CreateGuest().GetAsync("/api/products");
        var list = (await r.Content.ReadFromJsonAsync<List<ProductDto>>())!;
        Assert.Equal(12, list.Count);
        Assert.Equal(Enumerable.Range(1, 12), list.Select(p => p.Id));
        Assert.Equal(new ProductDto(4, "TNC-004", "ลำโพงบลูทูธ BoomMini", list[3].Description, 1890m, "/products/tnc-004.svg", 3), list[3]);
        Assert.Equal(0, list.Single(p => p.Id == 11).Available);
        Assert.All(list, p => Assert.False(string.IsNullOrWhiteSpace(p.Description)));
        Assert.False(r.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Availability_drops_when_stock_is_reserved()
    {
        await f.ExecAsync($"UPDATE inventory.Stock SET Reserved = 2 WHERE ProductId = 4");
        var list = await f.CreateGuest().GetFromJsonAsync<List<ProductDto>>("/api/products");
        Assert.Equal(1, list!.Single(p => p.Id == 4).Available);
    }

    [Fact]
    public async Task Image_url_outside_products_folder_is_rejected_by_database() =>
        await Assert.ThrowsAsync<SqlException>(() =>
            f.ExecAsync($"UPDATE catalog.Products SET ImageUrl = {"javascript:alert(1)"} WHERE Id = 1"));

    [Fact]
    public async Task Negative_price_is_rejected_by_database() =>
        await Assert.ThrowsAsync<SqlException>(() => f.ExecAsync($"UPDATE catalog.Products SET Price = -1 WHERE Id = 1"));

    [Fact]
    public async Task Script_in_product_name_is_returned_as_json_text()
    {
        await f.ExecAsync($"UPDATE catalog.Products SET Name = {"<script>alert(1)</script>"} WHERE Id = 1");
        var r = await f.CreateGuest().GetAsync("/api/products");
        Assert.Equal("application/json", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal("<script>alert(1)</script>", (await r.Content.ReadFromJsonAsync<List<ProductDto>>())![0].Name);
    }
}
