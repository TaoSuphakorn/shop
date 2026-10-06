using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Shop.Shared;

/// <summary>HMAC key for the guest cookie. Every pod must share the same key (Secret CART_SIGNING_KEY).</summary>
public sealed record GuestKey(byte[] Value)
{
    public static GuestKey FromBase64(string? configured)
    {
        var bytes = new byte[64];
        if (string.IsNullOrEmpty(configured) || !Convert.TryFromBase64String(configured, bytes, out var length) || length < 32)
            throw new InvalidOperationException("CART_SIGNING_KEY must be base64 of at least 32 random bytes.");
        return new GuestKey(bytes[..length]);
    }
}

/// <summary>
/// Guest identity (spec 11.1): cookie "sid" = "{guestId:N}.{base64url(HMAC-SHA256(guestId))}".
/// Carts and orders are always looked up by this id, so a guest can only ever reach their own.
/// </summary>
public static class Guest
{
    public const string CookieName = "sid";
    private static readonly object ItemKey = new();

    public static string CreateCookieValue(Guid guestId, byte[] key) =>
        $"{guestId:N}.{WebEncoders.Base64UrlEncode(HMACSHA256.HashData(key, guestId.ToByteArray()))}";

    public static bool TryParse(string? value, byte[] key, out Guid guestId)
    {
        guestId = default;
        var parts = value?.Split('.');
        if (parts is not { Length: 2 } || !Guid.TryParseExact(parts[0], "N", out var id)) return false;

        byte[] signature;
        try { signature = WebEncoders.Base64UrlDecode(parts[1]); }
        catch (FormatException) { return false; }

        if (!CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(key, id.ToByteArray()))) return false;
        guestId = id;
        return true;
    }

    public static Guid GetGuestId(this HttpContext http) =>
        http.Items[ItemKey] as Guid? ?? throw new InvalidOperationException("Endpoint is not under RequireGuest().");

    /// <summary>Resolves the guest from the cookie; issues a new identity when it is missing or forged.</summary>
    public static RouteGroupBuilder RequireGuest(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var key = http.RequestServices.GetRequiredService<GuestKey>().Value;
            if (!TryParse(http.Request.Cookies[CookieName], key, out var guestId))
            {
                guestId = Guid.NewGuid();
                http.Response.Cookies.Append(CookieName, CreateCookieValue(guestId, key), new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Path = "/",
                    MaxAge = TimeSpan.FromDays(30),
                });
            }
            http.Items[ItemKey] = guestId;
            return await next(context);
        });
        return group;
    }
}
