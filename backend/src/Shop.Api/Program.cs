using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Shop.Api;
using Shop.Cart;
using Shop.Catalog;
using Shop.Inventory;
using Shop.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 4096);
builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.Configure<RouteHandlerOptions>(route => route.ThrowOnBadRequest = true);   // bad bodies reach UseShopErrors
builder.Services.AddShared().AddInventoryModule().AddCatalogModule().AddCartModule();

var app = builder.Build();

if (args.Contains("migrate"))
{
    await DbProvisioner.RunAsync(app.Services);
    return;
}

app.Services.GetRequiredService<GuestKey>();   // fail fast when the signing key is missing or weak

app.UseShopErrors();

app.MapCatalogEndpoints();
app.MapCartEndpoints();

app.MapGet("/healthz/live", () => Results.Ok());
app.MapGet("/healthz/ready", async (IConfiguration config, CancellationToken ct) =>
{
    try
    {
        await using var connection = new SqlConnection(config.GetConnectionString("Shop"));
        await connection.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT 1", connection);
        await cmd.ExecuteScalarAsync(ct);
        return Results.Ok();
    }
    catch (SqlException)
    {
        return Results.StatusCode(503);
    }
});

app.Run();

public partial class Program;
