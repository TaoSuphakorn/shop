using Shop.Cart;
using Shop.Shared;

namespace Shop.UnitTests;

public class CartRulesTests
{
    private static readonly ProductInfo Mouse = new(3, "TNC-003", "Mouse", "d", 1290m, "/products/tnc-003.svg");
    private static readonly ProductInfo Cable = new(9, "TNC-009", "Cable", "d", 390.50m, "/products/tnc-009.svg");

    [Theory]
    [InlineData(1)]
    [InlineData(99)]
    public void Quantity_within_range_is_accepted(int quantity) => CartRules.ValidateQuantity(quantity);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void Quantity_out_of_range_is_validation_failed(int quantity)
    {
        var ex = Assert.Throws<ApiException>(() => CartRules.ValidateQuantity(quantity));
        Assert.Equal(400, ex.Status);
        Assert.Equal("validation_failed", ex.Code);
    }

    [Fact]
    public void Line_limit_allows_fifty_lines_and_rejects_the_fifty_first()
    {
        CartRules.ValidateLineCount(50);
        Assert.Equal("validation_failed", Assert.Throws<ApiException>(() => CartRules.ValidateLineCount(51)).Code);
    }

    [Fact]
    public void Dto_computes_line_totals_and_total_from_catalog_prices()
    {
        var expires = DateTime.UtcNow;
        var dto = CartRules.ToDto(
            [new StockLine(9, 3), new StockLine(3, 2)], [Mouse, Cable],
            new Dictionary<int, int> { [3] = 7, [9] = 0 }, expires);

        Assert.Equal([3, 9], dto.Items.Select(i => i.ProductId));
        Assert.Equal(2580m, dto.Items[0].LineTotal);
        Assert.Equal(1171.50m, dto.Items[1].LineTotal);
        Assert.Equal(3751.50m, dto.Total);
        Assert.Equal(7, dto.Items[0].Available);
        Assert.Equal(0, dto.Items[1].Available);
        Assert.Equal(expires, dto.ExpiresAt);
    }

    [Fact]
    public void Empty_cart_has_zero_total_and_no_expiry()
    {
        var dto = CartRules.ToDto([], [], new Dictionary<int, int>(), DateTime.UtcNow);
        Assert.Empty(dto.Items);
        Assert.Equal(0m, dto.Total);
        Assert.Null(dto.ExpiresAt);
    }
}
