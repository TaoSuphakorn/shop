using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Shop.Shared;

/// <summary>
/// Makes a POST safe to retry (spec 10.1). The key row is inserted in the SAME transaction as the
/// business change, so:
///  - a concurrent duplicate blocks on the primary key until the first request commits, then replays its response;
///  - a failed request rolls the row back with everything else, and its retry runs again from scratch.
/// </summary>
public static partial class Idempotency
{
    public const string Header = "Idempotency-Key";
    private const int DuplicateKey = 2627;

    [GeneratedRegex(@"^[A-Za-z0-9_-]{16,64}\z")]   // \z, not $: "$" would also accept a trailing newline
    private static partial Regex KeyPattern();

    public static bool IsValidKey(string? key) => key is not null && KeyPattern().IsMatch(key);

    public static byte[] RequestHash(string method, string path, ReadOnlySpan<byte> body)
    {
        var prefix = Encoding.UTF8.GetBytes($"{method} {path}\n");
        return SHA256.HashData([.. prefix, .. body]);
    }

    /// <summary>The filter reads the body after model binding consumed it, so it must be buffered up front.</summary>
    public static IApplicationBuilder UseIdempotencyBuffering(this IApplicationBuilder app) => app.Use((http, next) =>
    {
        if (http.Request.Headers.ContainsKey(Header)) http.Request.EnableBuffering();
        return next(http);
    });

    /// <summary>Handlers under this filter must return TypedResults.Ok/Created and signal errors by throwing ApiException.</summary>
    public static RouteHandlerBuilder RequireIdempotencyKey(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var key = http.Request.Headers[Header].ToString();
            if (!IsValidKey(key))
                throw new ApiException(400, "idempotency_key_required", $"{Header} header must be 16-64 characters of A-Z a-z 0-9 _ -");

            var guestId = http.GetGuestId();
            http.Request.Body.Position = 0;
            using var body = new MemoryStream();
            await http.Request.Body.CopyToAsync(body, http.RequestAborted);
            var hash = RequestHash(http.Request.Method, http.Request.Path, body.ToArray());

            var db = http.RequestServices.GetRequiredService<SharedDbContext>();
            var json = http.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;

            return await http.RequestServices.GetRequiredService<UnitOfWork>().ExecuteAsync<object?>(async ct =>
            {
                try
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT shared.IdempotencyKeys (GuestId, [Key], RequestHash, StatusCode, CreatedAt)
                        VALUES ({guestId}, {key}, {hash}, 0, SYSUTCDATETIME())
                        """, ct);
                }
                catch (Exception ex) when (UnitOfWork.SqlError(ex)?.Number == DuplicateKey)
                {
                    var stored = await db.IdempotencyKeys.AsNoTracking().SingleAsync(k => k.GuestId == guestId && k.Key == key, ct);
                    if (!CryptographicOperations.FixedTimeEquals(stored.RequestHash, hash))
                        throw new ApiException(422, "idempotency_key_reuse", "this key was already used with a different request");
                    return Results.Content(stored.ResponseBody, "application/json", statusCode: stored.StatusCode);
                }

                if (await next(context) is not (IValueHttpResult value and IStatusCodeHttpResult status))
                    throw new InvalidOperationException("Idempotent handlers must return TypedResults.Ok or TypedResults.Created.");

                var statusCode = status.StatusCode ?? 200;
                var responseBody = JsonSerializer.Serialize(value.Value, json);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE shared.IdempotencyKeys SET StatusCode = {statusCode}, ResponseBody = {responseBody}
                    WHERE GuestId = {guestId} AND [Key] = {key}
                    """, ct);
                return Results.Content(responseBody, "application/json", statusCode: statusCode);   // same bytes as a replay
            }, http.RequestAborted);
        });
}

/// <summary>Keys only need to outlive client retries; a day is plenty (spec 6).</summary>
public sealed class IdempotencyCleanup(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<IdempotencyCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("BackgroundJobs:Enabled", true)) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Idempotency key cleanup failed"); }
            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SharedDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"DELETE shared.IdempotencyKeys WHERE CreatedAt < DATEADD(hour, -24, SYSUTCDATETIME())", ct);
    }
}
