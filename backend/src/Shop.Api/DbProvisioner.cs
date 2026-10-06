using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shop.Shared;

namespace Shop.Api;

/// <summary>Run by the "migrate" command (a Kubernetes Job), never at API startup.</summary>
public static class DbProvisioner
{
    private static readonly string[] Schemas = ["catalog", "inventory", "cart", "ordering", "shared"];

    public static async Task RunAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var connectionString = scope.ServiceProvider.GetRequiredService<IConfiguration>().GetConnectionString("Shop")!;
        await CreateDatabaseAsync(connectionString);
        foreach (var context in scope.ServiceProvider.GetServices<ModuleDbContext>())
            await context.Database.MigrateAsync();

        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        if (config["APP_DB_PASSWORD"] is { Length: > 0 } password)
            await CreateAppLoginAsync(config.GetConnectionString("Shop")!, password);
    }

    // EF cannot create the database itself here: it would clone the shared connection, whose password
    // is no longer readable once opened. The name comes from configuration and is bracket-quoted.
    private static async Task CreateDatabaseAsync(string connectionString)
    {
        var target = new SqlConnectionStringBuilder(connectionString);
        var name = target.InitialCatalog.Replace("]", "]]");
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new SqlCommand($"IF DB_ID(@name) IS NULL CREATE DATABASE [{name}]", connection);
        cmd.Parameters.AddWithValue("@name", target.InitialCatalog);
        await cmd.ExecuteNonQueryAsync();
    }

    // CREATE LOGIN cannot take a parameter. This is the only place SQL is built from a string:
    // the password comes from the operator's secret, never from a request, and quotes are escaped.
    private static async Task CreateAppLoginAsync(string connectionString, string password)
    {
        var quoted = password.Replace("'", "''");
        var grants = string.Join("\n", Schemas.Select(s =>
            $"IF SCHEMA_ID('{s}') IS NOT NULL GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[{s}] TO shop_app;"));
        var sql = $"""
            IF SUSER_ID('shop_app') IS NULL CREATE LOGIN shop_app WITH PASSWORD = '{quoted}';
            ELSE ALTER LOGIN shop_app WITH PASSWORD = '{quoted}';
            IF USER_ID('shop_app') IS NULL CREATE USER shop_app FOR LOGIN shop_app;
            {grants}
            """;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }
}
