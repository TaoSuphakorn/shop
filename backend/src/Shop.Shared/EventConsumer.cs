using System.Text;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shop.Shared;

/// <summary>A module's reaction to an event. Runs inside a transaction; throw to have it retried.</summary>
public interface IEventHandler
{
    string EventType { get; }
    /// <summary>Unique name, stored with the message id to make the handler run once per message.</summary>
    string Name { get; }
    Task HandleAsync(string payloadJson, CancellationToken ct);
}

/// <summary>
/// Reads events from Kafka and runs the handlers (spec 10.2). The offset is committed only after the
/// handlers' transaction commits, so a crash redelivers the message; the ProcessedMessages row,
/// written in the same transaction as the handler's work, turns that redelivery into a no-op.
/// A message that cannot be handled goes to the dead-letter topic so it does not block its partition.
/// </summary>
public sealed class EventConsumer(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<EventConsumer> logger) : BackgroundService
{
    private const int DuplicateKey = 2627, MaxAttempts = 5;
    private readonly string _bootstrapServers = EventBus.BootstrapServers(configuration);
    private IConsumer<string, string>? _consumer;
    private IProducer<string, string>? _deadLetters;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("BackgroundJobs:Enabled", true)) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeOnceAsync(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Event consumer failed; retrying");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    /// <summary>Waits up to <paramref name="timeout"/> for one message and handles it. Returns false when none arrived.</summary>
    public async Task<bool> ConsumeOnceAsync(TimeSpan timeout, CancellationToken ct)
    {
        await EventBus.EnsureTopicsAsync(_bootstrapServers);
        _consumer ??= CreateConsumer();

        var result = await Task.Run(() => _consumer.Consume(timeout), ct);   // Consume blocks its thread
        if (result?.Message is not { } message) return false;

        var type = Header(message, EventBus.TypeHeader);
        if (!Guid.TryParse(Header(message, EventBus.MessageIdHeader), out var messageId) || type is null)
            await DeadLetterAsync(message, "missing message-id or type header");
        else if (!await HandleAsync(messageId, type, message.Value, ct))
            await DeadLetterAsync(message, $"no handler succeeded for type '{type}'");

        _consumer.Commit(result);
        return true;
    }

    private async Task<bool> HandleAsync(Guid messageId, string type, string payload, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var handlers = scope.ServiceProvider.GetServices<IEventHandler>().Where(h => h.EventType == type).ToList();
                if (handlers.Count == 0) return false;

                var db = scope.ServiceProvider.GetRequiredService<SharedDbContext>();
                await scope.ServiceProvider.GetRequiredService<UnitOfWork>().ExecuteAsync(async c =>
                {
                    foreach (var handler in handlers)
                    {
                        try
                        {
                            await db.Database.ExecuteSqlInterpolatedAsync(
                                $"INSERT shared.ProcessedMessages (MessageId, Handler) VALUES ({messageId}, {handler.Name})", c);
                        }
                        catch (Exception ex) when (UnitOfWork.SqlError(ex)?.Number == DuplicateKey)
                        {
                            continue;   // this handler already processed this message
                        }
                        await handler.HandleAsync(payload, c);
                    }
                }, ct);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < MaxAttempts)
            {
                logger.LogWarning(ex, "Handling message {MessageId} ({Type}) failed, attempt {Attempt}", messageId, type, attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Handling message {MessageId} ({Type}) failed {Attempts} times", messageId, type, MaxAttempts);
                return false;
            }
        }
    }

    private async Task DeadLetterAsync(Message<string, string> message, string reason)
    {
        logger.LogError("Sending message with key {Key} to {Topic}: {Reason}", message.Key, EventBus.DeadLetterTopic, reason);
        _deadLetters ??= new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = _bootstrapServers, Acks = Acks.All }).Build();
        var headers = new Headers();
        foreach (var header in message.Headers) headers.Add(header.Key, header.GetValueBytes());
        headers.Add("dead-letter-reason", Encoding.UTF8.GetBytes(reason));
        await _deadLetters.ProduceAsync(EventBus.DeadLetterTopic, new Message<string, string> { Key = message.Key, Value = message.Value, Headers = headers });
    }

    private IConsumer<string, string> CreateConsumer()
    {
        var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _bootstrapServers,
            GroupId = configuration["Kafka:GroupId"] ?? "shop-inventory",
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).SetLogHandler((_, _) => { }).Build();
        consumer.Subscribe(EventBus.Topic);
        return consumer;
    }

    private static string? Header(Message<string, string> message, string name) =>
        message.Headers.TryGetLastBytes(name, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;

    public override void Dispose()
    {
        _consumer?.Close();
        _consumer?.Dispose();
        _deadLetters?.Dispose();
        base.Dispose();
    }
}
