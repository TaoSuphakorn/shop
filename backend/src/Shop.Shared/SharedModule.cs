using Microsoft.Extensions.DependencyInjection;

namespace Shop.Shared;

public static class SharedModule
{
    public static IServiceCollection AddShared(this IServiceCollection services) =>
        services
            .AddScoped<UnitOfWork>()
            .AddModuleDbContext<SharedDbContext>("shared");
}
