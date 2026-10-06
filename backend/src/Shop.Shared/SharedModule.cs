using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shop.Shared;

public static class SharedModule
{
    public static IServiceCollection AddShared(this IServiceCollection services) =>
        services
            .AddScoped<UnitOfWork>()
            .AddModuleDbContext<SharedDbContext>("shared")
            .AddSingleton(sp => GuestKey.FromBase64(sp.GetRequiredService<IConfiguration>()["CART_SIGNING_KEY"]));
}
