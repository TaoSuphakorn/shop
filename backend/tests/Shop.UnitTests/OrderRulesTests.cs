using Shop.Ordering;
using Shop.Shared;

namespace Shop.UnitTests;

public class OrderRulesTests
{
    private static readonly ProductInfo Speaker = new(4, "TNC-004", "Speaker", "d", 1890m, "/products/tnc-004.svg");
    private static readonly ProductInfo Cable = new(9, "TNC-009", "Cable", "d", 390.25m, "/products/tnc-009.svg");
    private static readonly Guid Guest = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 6, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Order_is_paid_and_totals_unit_price_times_quantity()
    {
        var order = OrderRules.Create(Guest, [new StockLine(9, 3), new StockLine(4, 2)], [Speaker, Cable], Now);

        Assert.Equal("Paid", order.Status);
        Assert.Equal(Guest, order.GuestId);
        Assert.Equal(Now, order.CreatedAt);
        Assert.Null(order.CancelledAt);
        Assert.NotEqual(Guid.Empty, order.Id);
        Assert.Equal(4950.75m, order.Total);   // 1890*2 + 390.25*3
        Assert.Equal(order.Total, order.Items.Sum(i => i.LineTotal));
    }

    [Fact]
    public void Items_snapshot_the_product_at_purchase_time_in_product_order()
    {
        var order = OrderRules.Create(Guest, [new StockLine(9, 3), new StockLine(4, 2)], [Speaker, Cable], Now);

        Assert.Equal([4, 9], order.Items.Select(i => i.ProductId));
        var cable = order.Items[1];
        Assert.Equal(("TNC-009", "Cable", 390.25m, 3, 1170.75m), (cable.Sku, cable.Name, cable.UnitPrice, cable.Quantity, cable.LineTotal));
    }

    [Fact]
    public void Empty_cart_is_cart_empty()
    {
        var ex = Assert.Throws<ApiException>(() => OrderRules.Create(Guest, [], [], Now));
        Assert.Equal((400, "cart_empty"), (ex.Status, ex.Code));
    }

    [Fact]
    public void Line_for_a_product_missing_from_the_catalog_fails_loudly() =>
        Assert.Throws<KeyNotFoundException>(() => OrderRules.Create(Guest, [new StockLine(77, 1)], [Speaker], Now));

    [Fact]
    public void Dto_mirrors_the_order()
    {
        var order = OrderRules.Create(Guest, [new StockLine(4, 1)], [Speaker], Now);
        var dto = OrderRules.ToDto(order);
        Assert.Equal((order.Id, "Paid", 1890m, Now), (dto.Id, dto.Status, dto.Total, dto.CreatedAt));
        Assert.Equal(new OrderItemDto(4, "TNC-004", "Speaker", 1890m, 1, 1890m), Assert.Single(dto.Items));
    }
}
