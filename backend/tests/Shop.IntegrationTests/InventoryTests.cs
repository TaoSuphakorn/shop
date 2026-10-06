using Microsoft.Extensions.DependencyInjection;
using Shop.Shared;

namespace Shop.IntegrationTests;

[Collection("shop")]
public class InventoryTests(ShopFactory f) : IAsyncLifetime
{
    public Task InitializeAsync() => f.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task Run(Func<IInventoryApi, Task> work)
    {
        using var scope = f.Services.CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryApi>();
        await scope.ServiceProvider.GetRequiredService<UnitOfWork>().ExecuteAsync(_ => work(inventory), default);
    }

    [Fact]
    public async Task Reserve_then_commit_moves_stock()
    {
        await Run(i => i.ReserveAsync(4, 2, default));
        Assert.Equal((3, 2), await f.StockAsync(4));
        await Run(i => i.CommitAsync([new(4, 2)], default));
        Assert.Equal((1, 0), await f.StockAsync(4));
    }

    [Fact]
    public async Task Reserve_then_release_restores_availability()
    {
        await Run(i => i.ReserveAsync(4, 3, default));
        await Run(i => i.ReleaseAsync([new(4, 2)], default));
        Assert.Equal((3, 1), await f.StockAsync(4));
    }

    [Fact]
    public async Task Reserve_more_than_available_throws_out_of_stock_and_changes_nothing()
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() => Run(i => i.ReserveAsync(7, 2, default)));
        Assert.Equal(409, ex.Status);
        Assert.Equal("out_of_stock", ex.Code);
        Assert.Equal(1, ex.Extensions!["available"]);
        Assert.Equal((1, 0), await f.StockAsync(7));
    }

    [Fact]
    public async Task Fifty_parallel_reserves_of_five_units_succeed_exactly_five_times()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(async _ =>
        {
            try { await Run(i => i.ReserveAsync(10, 1, default)); return true; }
            catch (ApiException) { return false; }
        }));
        Assert.Equal(5, results.Count(ok => ok));
        Assert.Equal((5, 5), await f.StockAsync(10));
    }

    [Fact]
    public async Task Release_more_than_reserved_throws() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(i => i.ReleaseAsync([new(1, 1)], default)));

    [Fact]
    public async Task Commit_more_than_reserved_throws_and_changes_nothing()
    {
        await Run(i => i.ReserveAsync(4, 1, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(i => i.CommitAsync([new(4, 2)], default)));
        Assert.Equal((3, 1), await f.StockAsync(4));
    }

    [Fact]
    public async Task Restock_adds_on_hand()
    {
        await Run(i => i.RestockAsync([new(11, 4)], default));
        Assert.Equal((4, 0), await f.StockAsync(11));
    }

    [Fact]
    public async Task Availability_is_on_hand_minus_reserved()
    {
        await Run(i => i.ReserveAsync(4, 1, default));
        IReadOnlyDictionary<int, int> a = null!;
        await Run(async i => a = await i.GetAvailabilityAsync([4, 11], default));
        Assert.Equal(2, a[4]);
        Assert.Equal(0, a[11]);
    }

    [Fact]
    public async Task Database_rejects_reserved_above_on_hand() =>
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            f.ExecAsync($"UPDATE inventory.Stock SET Reserved = OnHand + 1 WHERE ProductId = 1"));
}
