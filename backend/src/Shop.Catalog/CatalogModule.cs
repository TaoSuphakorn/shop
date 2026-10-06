using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Shared;

namespace Shop.Catalog;

public record ProductDto(int Id, string Sku, string Name, string Description, decimal Price, string ImageUrl, int Available);

public sealed class CatalogApi(CatalogDbContext db) : ICatalogApi
{
    public async Task<IReadOnlyList<ProductInfo>> GetProductsAsync(IReadOnlyCollection<int>? ids, CancellationToken ct)
    {
        var query = db.Products.AsNoTracking();
        if (ids is not null) query = query.Where(p => ids.Contains(p.Id));
        return await query.OrderBy(p => p.Id)
            .Select(p => new ProductInfo(p.Id, p.Sku, p.Name, p.Description, p.Price, p.ImageUrl))
            .ToListAsync(ct);
    }
}

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services) =>
        services
            .AddModuleDbContext<CatalogDbContext>("catalog")
            .AddScoped<ICatalogApi, CatalogApi>();

    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        // Public and cookie-free: the Next.js server calls this without a guest identity.
        app.MapGet("/api/products", async (ICatalogApi catalog, IInventoryApi inventory, CancellationToken ct) =>
        {
            var products = await catalog.GetProductsAsync(null, ct);
            var available = await inventory.GetAvailabilityAsync(null, ct);
            return products.Select(p => new ProductDto(
                p.Id, p.Sku, p.Name, p.Description, p.Price, p.ImageUrl, available.GetValueOrDefault(p.Id)));
        });
        return app;
    }
}
