using Microsoft.Extensions.DependencyInjection;
using Shop.Shared;

namespace Shop.Inventory;

public static class InventoryModule
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services) =>
        services
            .AddModuleDbContext<InventoryDbContext>("inventory")
            .AddScoped<IInventoryApi, InventoryApi>();
}
