using Microsoft.AspNetCore.Mvc;
using Shop.Shared;

namespace Shop.Api;

public static class ErrorHandling
{
    /// <summary>Every error leaves as ProblemDetails with a machine-readable "code"; internals stay in the log.</summary>
    public static IApplicationBuilder UseShopErrors(this WebApplication app)
    {
        app.UseStatusCodePages(statusCode => WriteAsync(statusCode.HttpContext, statusCode.HttpContext.Response.StatusCode, null, null));
        return app.Use(async (http, next) =>
        {
            try
            {
                await next(http);
            }
            catch (Exception ex) when (!http.Response.HasStarted)
            {
                switch (ex)
                {
                    case ApiException api:
                        await WriteAsync(http, api.Status, api.Code, api.Extensions);
                        break;
                    case BadHttpRequestException bad:
                        await WriteAsync(http, bad.StatusCode == 413 ? 413 : 400, "validation_failed", null);
                        break;
                    default:
                        app.Logger.LogError(ex, "Unhandled error on {Method} {Path}", http.Request.Method, http.Request.Path);
                        await WriteAsync(http, 500, "internal_error", null);
                        break;
                }
            }
        });
    }

    public static string DefaultCode(int status) => status switch
    {
        404 or 405 => "not_found",
        429 => "rate_limited",
        >= 500 => "internal_error",
        _ => "validation_failed",
    };

    public static Task WriteAsync(HttpContext http, int status, string? code, IReadOnlyDictionary<string, object?>? extensions)
    {
        var problem = new ProblemDetails { Status = status };
        problem.Extensions["code"] = code ?? DefaultCode(status);
        foreach (var (key, value) in extensions ?? new Dictionary<string, object?>()) problem.Extensions[key] = value;
        http.Response.StatusCode = status;
        return http.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
    }
}
