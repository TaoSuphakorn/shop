using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shop.Shared;

namespace Shop.Cart;

/// <summary>
/// Returns the stock held by abandoned carts (spec 7). Runs in every pod: READPAST makes pods skip a
/// cart another pod (or the cart's own request) has locked instead of waiting on it.
/// </summary>
public sealed class CartSweeper(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<CartSweeper> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("BackgroundJobs:Enabled", true)) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await RunOnceAsync(stoppingToken)) { }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Cart sweep failed; retrying on the next interval");
            }
            await Task.Delay(Interval, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    /// <summary>Releases one expired cart. Returns false when there is none left.</summary>
    public async Task<bool> RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CartDbContext>();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryApi>();

        return await scope.ServiceProvider.GetRequiredService<UnitOfWork>().ExecuteAsync(async c =>
        {
            var cart = await db.Carts
                .FromSqlInterpolated($"""
                    SELECT TOP 1 * FROM cart.Carts WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE ExpiresAt < SYSUTCDATETIME() ORDER BY ExpiresAt
                    """)
                .AsNoTracking().ToListAsync(c);
            if (cart.Count == 0) return false;

            var guestId = cart[0].GuestId;
            var lines = await db.CartItems.Where(i => i.GuestId == guestId)
                .Select(i => new StockLine(i.ProductId, i.Quantity)).ToListAsync(c);
            await inventory.ReleaseAsync(lines, c);
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE cart.Carts WHERE GuestId = {guestId}", c);
            return true;
        }, ct);
    }
}
