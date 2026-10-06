using System.Text;
using Shop.Shared;

namespace Shop.UnitTests;

public class IdempotencyTests
{
    private static byte[] Hash(string method, string path, string body) =>
        Idempotency.RequestHash(method, path, Encoding.UTF8.GetBytes(body));

    [Theory]
    [InlineData("0123456789abcdef")]                       // 16 chars: shortest allowed
    [InlineData("3f2b8c1e-9a4d-4e6f-8b7a-1c2d3e4f5a6b")]   // a UUID
    [InlineData("AZaz09_-AZaz09_-")]
    public void Accepts_keys_of_16_to_64_safe_characters(string key) => Assert.True(Idempotency.IsValidKey(key));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("0123456789abcde")]            // 15 chars
    [InlineData("x' OR '1'='1' -- pad pad")]   // SQL injection attempt
    [InlineData("0123456789abcdef<script>")]
    [InlineData("0123456789abcdef\n")]
    public void Rejects_other_keys(string? key) => Assert.False(Idempotency.IsValidKey(key));

    [Fact]
    public void Rejects_a_key_longer_than_64_characters() => Assert.False(Idempotency.IsValidKey(new string('a', 65)));

    [Fact]
    public void Hash_is_32_bytes_and_stable() =>
        Assert.Equal(Hash("POST", "/api/orders", "{}"), Hash("POST", "/api/orders", "{}"));

    [Fact]
    public void Hash_changes_with_method_path_or_body()
    {
        var baseline = Hash("POST", "/api/cart/items", """{"productId":1,"quantity":1}""");
        Assert.Equal(32, baseline.Length);
        Assert.NotEqual(baseline, Hash("PUT", "/api/cart/items", """{"productId":1,"quantity":1}"""));
        Assert.NotEqual(baseline, Hash("POST", "/api/orders", """{"productId":1,"quantity":1}"""));
        Assert.NotEqual(baseline, Hash("POST", "/api/cart/items", """{"productId":1,"quantity":2}"""));
    }
}
