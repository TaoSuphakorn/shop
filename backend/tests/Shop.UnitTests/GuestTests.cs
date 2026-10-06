using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shop.Shared;

namespace Shop.UnitTests;

public class GuestTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void Round_trips()
    {
        var id = Guid.NewGuid();
        Assert.True(Guest.TryParse(Guest.CreateCookieValue(id, Key), Key, out var parsed));
        Assert.Equal(id, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("abc.def")]
    [InlineData("00000000000000000000000000000000.")]
    [InlineData("00000000000000000000000000000000.!!!not-base64!!!")]
    public void Rejects_malformed(string? value) => Assert.False(Guest.TryParse(value, Key, out _));

    [Fact]
    public void Rejects_tampered_signature()
    {
        var value = Guest.CreateCookieValue(Guid.NewGuid(), Key);
        var tampered = value[..^1] + (value[^1] == 'A' ? 'B' : 'A');
        Assert.False(Guest.TryParse(tampered, Key, out _));
    }

    [Fact]
    public void Rejects_signature_moved_to_another_guest()
    {
        var signature = Guest.CreateCookieValue(Guid.NewGuid(), Key).Split('.')[1];
        Assert.False(Guest.TryParse($"{Guid.NewGuid():N}.{signature}", Key, out _));
    }

    [Fact]
    public void Rejects_value_signed_with_another_key() =>
        Assert.False(Guest.TryParse(Guest.CreateCookieValue(Guid.NewGuid(), Key), RandomNumberGenerator.GetBytes(32), out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64")]
    [InlineData("c2hvcnQ=")]   // "short": fewer than 32 bytes
    public void Signing_key_must_be_at_least_32_bytes_of_base64(string? configured) =>
        Assert.Throws<InvalidOperationException>(() => GuestKey.FromBase64(configured));

    private static async Task<HttpClient> WhoAmIApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new GuestKey(Key));
        var app = builder.Build();
        app.MapGroup("/t").RequireGuest().MapGet("/whoami", (HttpContext http) => http.GetGuestId().ToString());
        await app.StartAsync();
        return app.GetTestClient();
    }

    [Fact]
    public async Task First_request_gets_a_cookie_with_security_attributes()
    {
        var response = await (await WhoAmIApp()).GetAsync("/t/whoami");
        var cookie = response.Headers.GetValues("Set-Cookie").Single().ToLowerInvariant();
        foreach (var attribute in new[] { "sid=", "httponly", "secure", "samesite=strict", "path=/", "max-age=2592000" })
            Assert.Contains(attribute, cookie);
    }

    [Fact]
    public async Task Valid_cookie_keeps_identity_and_is_not_reissued()
    {
        var id = Guid.NewGuid();
        var request = new HttpRequestMessage(HttpMethod.Get, "/t/whoami");
        request.Headers.Add("Cookie", $"sid={Guest.CreateCookieValue(id, Key)}");
        var response = await (await WhoAmIApp()).SendAsync(request);
        Assert.Equal(id.ToString(), await response.Content.ReadAsStringAsync());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Forged_cookie_gets_a_new_identity()   // spec 15 case 8
    {
        var forgedId = Guid.NewGuid();
        var request = new HttpRequestMessage(HttpMethod.Get, "/t/whoami");
        request.Headers.Add("Cookie", $"sid={Guest.CreateCookieValue(forgedId, RandomNumberGenerator.GetBytes(32))}");
        var response = await (await WhoAmIApp()).SendAsync(request);
        Assert.NotEqual(forgedId.ToString(), await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.Contains("Set-Cookie"));
    }
}
