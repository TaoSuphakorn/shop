using System.Text.Json;
using Shop.Inventory;
using Shop.Shared;

namespace Shop.UnitTests;

public class OrderCancelledHandlerTests
{
    private sealed class RecordingInventory : IInventoryApi
    {
        public List<StockLine> Restocked { get; } = [];
        public Task RestockAsync(IReadOnlyCollection<StockLine> lines, CancellationToken ct) { Restocked.AddRange(lines); return Task.CompletedTask; }
        public Task ReserveAsync(int productId, int quantity, CancellationToken ct) => throw new NotSupportedException();
        public Task ReleaseAsync(IReadOnlyCollection<StockLine> lines, CancellationToken ct) => throw new NotSupportedException();
        public Task CommitAsync(IReadOnlyCollection<StockLine> lines, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<int, int>> GetAvailabilityAsync(IReadOnlyCollection<int>? productIds, CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public void Handles_the_OrderCancelled_event() =>
        Assert.Equal("OrderCancelled", new OrderCancelledHandler(new RecordingInventory()).EventType);

    [Fact]
    public async Task Restocks_every_line_of_the_event_exactly_as_published()
    {
        var inventory = new RecordingInventory();
        var payload = JsonSerializer.Serialize(new OrderCancelled(Guid.NewGuid(), [new StockLine(4, 2), new StockLine(9, 3)]));

        await new OrderCancelledHandler(inventory).HandleAsync(payload, default);

        Assert.Equal([new StockLine(4, 2), new StockLine(9, 3)], inventory.Restocked);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("not json")]
    public async Task Unreadable_payload_throws_so_the_message_is_retried_then_dead_lettered(string payload) =>
        await Assert.ThrowsAnyAsync<Exception>(() => new OrderCancelledHandler(new RecordingInventory()).HandleAsync(payload, default));
}
