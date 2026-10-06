using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shop.Shared;

namespace Shop.Cart;

public static class CartModule
{
    public static IServiceCollection AddCartModule(this IServiceCollection services) =>
        services
            .AddModuleDbContext<CartDbContext>("cart")
            .AddScoped<CartService>()
            .AddScoped<ICartApi>(sp => sp.GetRequiredService<CartService>())
            .AddSingleton<CartSweeper>()
            .AddHostedService(sp => sp.GetRequiredService<CartSweeper>());

    public static IEndpointRouteBuilder MapCartEndpoints(this IEndpointRouteBuilder app)
    {
        // Every endpoint works on the caller's own cart; there is no cart id to tamper with.
        var cart = app.MapGroup("/api/cart").RequireGuest();

        cart.MapGet("/", (HttpContext http, CartService service, CancellationToken ct) =>
            service.GetAsync(http.GetGuestId(), ct));

        cart.MapPost("/items", async (AddItemRequest request, HttpContext http, CartService service, CancellationToken ct) =>
            TypedResults.Ok(await service.AddAsync(http.GetGuestId(), request.ProductId, request.Quantity, ct)))
            .RequireIdempotencyKey();

        cart.MapPut("/items/{productId:int}", (int productId, SetQuantityRequest request, HttpContext http, CartService service, CancellationToken ct) =>
            service.SetQuantityAsync(http.GetGuestId(), productId, request.Quantity, ct));

        cart.MapDelete("/items/{productId:int}", (int productId, HttpContext http, CartService service, CancellationToken ct) =>
            service.RemoveAsync(http.GetGuestId(), productId, ct));

        cart.MapDelete("/", (HttpContext http, CartService service, CancellationToken ct) =>
            service.ClearAsync(http.GetGuestId(), ct));

        return app;
    }
}
