using System.Net.Http.Json;
using System.Text.Json;

namespace Shop.IntegrationTests;

public static class HttpExtensions
{
    public static Task<HttpResponseMessage> PostKeyedAsync(this HttpClient client, string url, object? body = null, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    public static async Task<string> CodeAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString()!;
    }
}
