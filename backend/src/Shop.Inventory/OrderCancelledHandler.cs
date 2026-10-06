using System.Text.Json;
using Shop.Shared;

namespace Shop.Inventory;

/// <summary>A cancelled order puts its items back on the shelf.</summary>
public sealed class OrderCancelledHandler(IInventoryApi inventory) : IEventHandler
{
    public string EventType => nameof(OrderCancelled);
    public string Name => "Inventory.Restock";

    public Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var cancelled = JsonSerializer.Deserialize<OrderCancelled>(payloadJson)
            ?? throw new InvalidOperationException("OrderCancelled payload is empty.");
        return inventory.RestockAsync(cancelled.Items, ct);
    }
}
