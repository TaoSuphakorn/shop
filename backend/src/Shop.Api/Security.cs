using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Shop.Shared;
using IPNetwork = System.Net.IPNetwork;

namespace Shop.Api;

/// <summary>Edge protections of the API (spec 11.5). There is deliberately no CORS: the web app is same-origin.</summary>
public static class Security
{
    public const long MaxBodyBytes = 4096;

    public static IReadOnlyList<IPNetwork> ParseNetworks(string? configured) =>
        (configured ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(IPNetwork.Parse).ToList();

    public static bool IsTooLarge(long? contentLength) => contentLength > MaxBodyBytes;

    public static WebApplicationBuilder AddShopSecurity(this WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = MaxBodyBytes);

        // X-Forwarded-For is believed only from the ingress. Without this, anyone could fake their IP
        // and walk around the rate limits below.
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var network in ParseNetworks(builder.Configuration["ForwardedHeaders:KnownNetworks"]))
                options.KnownIPNetworks.Add(network);
        });

        // Counters live in each pod's memory, so the real ceiling is the limit times the replica count.
        // ponytail: per-pod counters; move to a shared store (Redis) if exact cluster-wide limits matter.
        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.OnRejected = async (context, _) =>
                await ErrorHandling.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests, "rate_limited", null);
            limiter.AddPolicy(RateLimits.Cart, http => PerIp(http, RateLimits.Cart, "RateLimit:CartPerMinute", 30));
            limiter.AddPolicy(RateLimits.Checkout, http => PerIp(http, RateLimits.Checkout, "RateLimit:CheckoutPerMinute", 5));
        });
        return builder;
    }

    // Partitioned by IP, not by guest: a guest can throw away the cookie and get a new identity for free.
    private static RateLimitPartition<string> PerIp(HttpContext http, string policy, string setting, int fallback)
    {
        var limit = http.RequestServices.GetRequiredService<IConfiguration>().GetValue(setting, fallback);
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter($"{policy}:{ip}",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1) });
    }

    public static WebApplication UseShopSecurity(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.Use(async (http, next) =>
        {
            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers.CacheControl = "no-store";

            var request = http.Request;
            if (IsTooLarge(request.ContentLength))
            {
                await ErrorHandling.WriteAsync(http, StatusCodes.Status413PayloadTooLarge, "validation_failed", null);
                return;
            }
            // JSON only. Besides being the contract, this is part of the CSRF defence: a cross-site HTML
            // form cannot send application/json.
            if (request.ContentLength > 0 && !request.HasJsonContentType())
            {
                await ErrorHandling.WriteAsync(http, StatusCodes.Status400BadRequest, "validation_failed", null);
                return;
            }
            await next(http);
        });
        app.UseRateLimiter();
        return app;
    }
}
