using System.Net;
using Shop.Api;

namespace Shop.UnitTests;

public class SecurityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void No_configured_networks_means_no_proxy_is_trusted(string? configured) =>
        Assert.Empty(Security.ParseNetworks(configured));

    [Fact]
    public void Parses_a_comma_separated_list_of_cidr_ranges()
    {
        var networks = Security.ParseNetworks("10.0.0.0/8, 192.168.65.0/24");
        Assert.Equal(2, networks.Count);
        Assert.True(networks[0].Contains(IPAddress.Parse("10.1.2.3")));
        Assert.False(networks[0].Contains(IPAddress.Parse("11.0.0.1")));
        Assert.True(networks[1].Contains(IPAddress.Parse("192.168.65.7")));
    }

    [Fact]
    public void A_malformed_range_fails_at_startup_instead_of_being_ignored() =>
        Assert.Throws<FormatException>(() => Security.ParseNetworks("10.0.0.0/8, not-a-network"));

    [Theory]
    [InlineData(0L, false)]
    [InlineData(4096L, false)]
    [InlineData(4097L, true)]
    [InlineData(null, false)]   // chunked: Kestrel's own limit applies while reading
    public void Body_is_too_large_above_4096_bytes(long? contentLength, bool tooLarge) =>
        Assert.Equal(tooLarge, Security.IsTooLarge(contentLength));
}
