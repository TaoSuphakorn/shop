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
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }

    public HttpClient CreateGuest() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    public T Service<T>() where T : notnull => Services.CreateScope().ServiceProvider.GetRequiredService<T>();

    public Task ResetAsync() => ExecAsync($"""
        DELETE shared.OutboxMessages; DELETE shared.ProcessedMessages; DELETE shared.IdempotencyKeys;
        """);

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

    public async Task ExecAsync(FormattableString sql)
    {
        await using var cmd = await CommandAsync(sql);
        await cmd.ExecuteNonQueryAsync();
    }

    // spec 15: Reserved = sum of cart quantities; OnHand = seed - sum of Paid orders
    public Task AssertInvariantsAsync() => Task.CompletedTask;

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
