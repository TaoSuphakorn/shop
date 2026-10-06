using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Shop.Api;
using Testcontainers.MsSql;

namespace Shop.IntegrationTests;

[CollectionDefinition("shop")]
public class ShopCollection : ICollectionFixture<ShopFactory>;

public sealed class ShopFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    private string ConnectionString =>
        new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = "Shop" }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Shop", ConnectionString);
        builder.UseSetting("CART_SIGNING_KEY", Convert.ToBase64String(new byte[32].Select((_, i) => (byte)i).ToArray()));
        builder.UseSetting("BackgroundJobs:Enabled", "false");
        builder.UseSetting("RateLimit:CartPerMinute", "100000");
        builder.UseSetting("RateLimit:CheckoutPerMinute", "100000");
    }

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();
        await DbProvisioner.RunAsync(Services);
        await ExecRawAsync("SELECT * INTO dbo.ProductsSeed FROM catalog.Products");   // snapshot for ResetAsync
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }

    public HttpClient CreateGuest() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    public T Service<T>() where T : notnull => Services.CreateScope().ServiceProvider.GetRequiredService<T>();

    public Task ResetAsync() => ExecRawAsync("""
        DELETE cart.Carts;
        DELETE ordering.Orders;
        DELETE shared.OutboxMessages; DELETE shared.ProcessedMessages; DELETE shared.IdempotencyKeys;
        UPDATE s SET OnHand = v.OnHand, Reserved = 0
        FROM inventory.Stock s JOIN (VALUES {SeedStockValues}) v(ProductId, OnHand) ON v.ProductId = s.ProductId;
        UPDATE p SET Name = s.Name, Description = s.Description, Price = s.Price, ImageUrl = s.ImageUrl
        FROM catalog.Products p JOIN dbo.ProductsSeed s ON s.Id = p.Id;
        """.Replace("{SeedStockValues}", string.Join(",", SeedOnHand.Select((onHand, i) => $"({i + 1},{onHand})"))));

    public static readonly int[] SeedOnHand = [25, 12, 40, 3, 8, 60, 1, 30, 100, 5, 0, 2];

    public async Task<(int OnHand, int Reserved)> StockAsync(int productId)
    {
        await using var cmd = await CommandAsync($"SELECT OnHand, Reserved FROM inventory.Stock WHERE ProductId = {productId}");
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"no stock row for product {productId}");
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    public async Task<T> ScalarAsync<T>(FormattableString sql)
    {
        await using var cmd = await CommandAsync(sql);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task ExecRawAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ExecAsync(FormattableString sql)
    {
        await using var cmd = await CommandAsync(sql);
        await cmd.ExecuteNonQueryAsync();
    }

    // spec 15: Reserved = sum of cart quantities; OnHand = seed - sum of Paid orders
    public async Task AssertInvariantsAsync()
    {
        var wrongReserved = await ScalarAsync<int>($"""
            SELECT COUNT(*) FROM inventory.Stock s
            WHERE s.Reserved <> (SELECT COALESCE(SUM(i.Quantity), 0) FROM cart.CartItems i WHERE i.ProductId = s.ProductId)
            """);
        Assert.Equal(0, wrongReserved);

        var seed = string.Join(",", SeedOnHand.Select((onHand, i) => $"({i + 1},{onHand})"));
        var wrongOnHand = (int)(await new Func<Task<object>>(async () =>
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var cmd = new SqlCommand($"""
                SELECT COUNT(*) FROM inventory.Stock s JOIN (VALUES {seed}) v(ProductId, OnHand) ON v.ProductId = s.ProductId
                WHERE s.OnHand <> v.OnHand - (SELECT COALESCE(SUM(i.Quantity), 0) FROM ordering.OrderItems i
                    JOIN ordering.Orders o ON o.Id = i.OrderId WHERE o.Status = 'Paid' AND i.ProductId = s.ProductId)
                """, connection);
            return (await cmd.ExecuteScalarAsync())!;
        })());
        Assert.Equal(0, wrongOnHand);
    }

    private async Task<SqlCommand> CommandAsync(FormattableString sql)
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var args = sql.GetArguments();
        var cmd = new SqlCommand(string.Format(sql.Format, args.Select((_, i) => (object)$"@p{i}").ToArray()), connection);
        for (var i = 0; i < args.Length; i++) cmd.Parameters.AddWithValue($"@p{i}", args[i] ?? DBNull.Value);
        cmd.Disposed += (_, _) => connection.Dispose();
        return cmd;
    }
}
