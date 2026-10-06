using System.Net;
using System.Net.Http.Json;
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shop.Cart;
using Shop.Ordering;
using Shop.Shared;

namespace Shop.IntegrationTests;

[Collection("shop")]
public class CancelTests(ShopFactory f) : IAsyncLifetime
{
    public Task InitializeAsync() => f.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<OrderDto> Buy(HttpClient c, int productId, int quantity)
    {
        await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(productId, quantity));
        return (await (await c.PostKeyedAsync("/api/orders")).Content.ReadFromJsonAsync<OrderDto>())!;
    }

    /// <summary>Publishes everything pending in the outbox, then consumes exactly that many messages.</summary>
    private async Task<int> Pump()
    {
        var published = 0;
        while (await f.Service<OutboxRelay>().RunOnceAsync(default)) published++;
        for (var i = 0; i < published; i++)
            Assert.True(await f.Service<EventConsumer>().ConsumeOnceAsync(TimeSpan.FromSeconds(60), default), "no message arrived from Kafka");
        return published;
    }

    [Fact]
    public async Task Cancel_returns_stock_after_event_is_consumed()
    {
        var c = f.CreateGuest();
        var o = await Buy(c, 4, 2);
        var r = await c.PostKeyedAsync($"/api/orders/{o.Id}/cancel");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cancelled = (await r.Content.ReadFromJsonAsync<OrderDto>())!;
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.NotNull(cancelled.CancelledAt);
        Assert.Equal((1, 0), await f.StockAsync(4));   // not yet: the event has not travelled through Kafka

        Assert.Equal(1, await Pump());
        Assert.Equal((3, 0), await f.StockAsync(4));
        Assert.Equal("Cancelled", (await c.GetFromJsonAsync<OrderDto>($"/api/orders/{o.Id}"))!.Status);
        await f.AssertInvariantsAsync();
    }

    [Fact]
    public async Task Double_cancel_writes_one_event_and_restocks_once()   // spec 15 case 4
    {
        var c = f.CreateGuest();
        var o = await Buy(c, 4, 2);
        var rs = await Task.WhenAll(c.PostKeyedAsync($"/api/orders/{o.Id}/cancel"), c.PostKeyedAsync($"/api/orders/{o.Id}/cancel"));
        Assert.All(rs, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, await f.ScalarAsync<int>($"SELECT COUNT(*) FROM shared.OutboxMessages"));
        await Pump();
        Assert.Equal((3, 0), await f.StockAsync(4));
    }

    [Fact]
    public async Task Redelivered_message_is_ignored()
    {
        var c = f.CreateGuest();
        var o = await Buy(c, 4, 1);
        await c.PostKeyedAsync($"/api/orders/{o.Id}/cancel");
        await Pump();
        await f.ExecAsync($"UPDATE shared.OutboxMessages SET ProcessedAt = NULL");   // the relay "forgot" it published
        Assert.Equal(1, await Pump());
        Assert.Equal((3, 0), await f.StockAsync(4));
    }

    [Fact]
    public async Task Event_is_published_with_order_id_key_and_headers()
    {
        var c = f.CreateGuest();
        var o = await Buy(c, 4, 1);
        await c.PostKeyedAsync($"/api/orders/{o.Id}/cancel");
        var row = await f.ScalarAsync<Guid>($"SELECT Id FROM shared.OutboxMessages");
        await Pump();

        var message = ConsumeFrom(EventBus.Topic, m => m.Key == o.Id.ToString());
        Assert.Equal("OrderCancelled", Header(message, "type"));
        Assert.Equal(row.ToString(), Header(message, "message-id"));
        Assert.Contains("\"ProductId\":4", message.Value);
        Assert.NotNull(await f.ScalarAsync<DateTime?>($"SELECT ProcessedAt FROM shared.OutboxMessages"));
    }

    [Fact]
    public async Task Another_guest_cannot_cancel()   // spec 15 case 7
    {
        var o = await Buy(f.CreateGuest(), 4, 1);
        var r = await f.CreateGuest().PostKeyedAsync($"/api/orders/{o.Id}/cancel");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal(0, await f.ScalarAsync<int>($"SELECT COUNT(*) FROM shared.OutboxMessages"));
        Assert.Equal("Paid", await f.ScalarAsync<string>($"SELECT Status FROM ordering.Orders"));
    }

    [Fact]
    public async Task Cancel_requires_an_idempotency_key()
    {
        var c = f.CreateGuest();
        var o = await Buy(c, 4, 1);
        Assert.Equal("idempotency_key_required", await (await c.PostAsync($"/api/orders/{o.Id}/cancel", null)).CodeAsync());
    }

    [Fact]
    public async Task Unknown_event_type_goes_to_dlq_and_does_not_block()   // Review Focus 4
    {
        var poison = Guid.NewGuid();
        await f.ExecAsync($"""
            INSERT shared.OutboxMessages (Id, Type, [Key], Payload, OccurredAt, Attempts)
            VALUES ({poison}, {"Unknown"}, {"k"}, {"{}"}, {DateTime.UtcNow.AddMinutes(-5)}, 0)
            """);
        var c = f.CreateGuest();
        var o = await Buy(c, 4, 1);
        await c.PostKeyedAsync($"/api/orders/{o.Id}/cancel");

        Assert.Equal(2, await Pump());
        Assert.Equal((3, 0), await f.StockAsync(4));
        var dead = ConsumeFrom(EventBus.DeadLetterTopic, m => Header(m, "message-id") == poison.ToString());
        Assert.Equal("Unknown", Header(dead, "type"));
    }

    [Fact]
    public async Task Relay_records_failed_attempt_when_kafka_is_unreachable()
    {
        var c = f.CreateGuest();
        var o = await Buy(c, 4, 1);
        await c.PostKeyedAsync($"/api/orders/{o.Id}/cancel");

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kafka:BootstrapServers"] = "localhost:1",
            ["Kafka:MessageTimeoutMs"] = "2000",
        }).Build();
        using var broken = new OutboxRelay(f.Services.GetRequiredService<IServiceScopeFactory>(), config, NullLogger<OutboxRelay>.Instance);
        Assert.False(await broken.RunOnceAsync(default));

        Assert.Equal(1, await f.ScalarAsync<int>($"SELECT Attempts FROM shared.OutboxMessages"));
        Assert.False(string.IsNullOrEmpty(await f.ScalarAsync<string>($"SELECT LastError FROM shared.OutboxMessages")));
        Assert.Equal(1, await f.ScalarAsync<int>($"SELECT COUNT(*) FROM shared.OutboxMessages WHERE ProcessedAt IS NULL"));

        Assert.Equal(1, await Pump());   // Kafka is back: the same row is delivered and nothing was lost
        Assert.Equal((3, 0), await f.StockAsync(4));
    }

    private static string? Header(Message<string, string> message, string name) =>
        message.Headers.TryGetLastBytes(name, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;

    private Message<string, string> ConsumeFrom(string topic, Func<Message<string, string>, bool> match)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = f.KafkaBootstrapServers,
            GroupId = $"test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(topic);
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
            if (consumer.Consume(TimeSpan.FromSeconds(1)) is { Message: { } message } && match(message)) return message;
        throw new Xunit.Sdk.XunitException($"no matching message on {topic}");
    }
}
