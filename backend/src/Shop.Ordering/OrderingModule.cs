using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shop.Shared;

namespace Shop.Ordering;

public static class OrderingModule
{
    public static IServiceCollection AddOrderingModule(this IServiceCollection services) =>
        services
            .AddModuleDbContext<OrderingDbContext>("ordering")
            .AddScoped<OrderService>();

    public static IEndpointRouteBuilder MapOrderingEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders").RequireGuest();

        // No request body: the order is built from the server-side cart and catalog prices only.
        orders.MapPost("/", async (HttpContext http, OrderService service, CancellationToken ct) =>
            {
                var order = await service.CheckoutAsync(http.GetGuestId(), ct);
                return TypedResults.Created($"/api/orders/{order.Id}", order);
            })
            .RequireIdempotencyKey()
            .RequireRateLimiting(RateLimits.Checkout);

        orders.MapGet("/{id:guid}", (Guid id, HttpContext http, OrderService service, CancellationToken ct) =>
            service.GetAsync(http.GetGuestId(), id, ct));

        orders.MapPost("/{id:guid}/cancel", async (Guid id, HttpContext http, OrderService service, CancellationToken ct) =>
                TypedResults.Ok(await service.CancelAsync(http.GetGuestId(), id, ct)))
            .RequireIdempotencyKey()
            .RequireRateLimiting(RateLimits.Checkout);

        return app;
    }
}
