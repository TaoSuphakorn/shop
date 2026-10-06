using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shop.Shared;

/// <summary>Names shared by the relay (producer side) and the consumer (spec 10.2).</summary>
public static class EventBus
{
    public const string Topic = "shop.events";
    public const string DeadLetterTopic = "shop.events.dlq";
    public const string MessageIdHeader = "message-id";
    public const string TypeHeader = "type";

    public static string BootstrapServers(IConfiguration configuration) =>
        configuration["Kafka:BootstrapServers"] ?? "localhost:9092";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HashSet<string> Ensured = [];

    /// <summary>Creates both topics once per broker address; a no-op when they already exist.</summary>
    public static async Task EnsureTopicsAsync(string bootstrapServers)
    {
        if (Ensured.Contains(bootstrapServers)) return;
        await Gate.WaitAsync();
        try
        {
            if (Ensured.Contains(bootstrapServers)) return;
            using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers, SocketTimeoutMs = 10000 }).Build();
            try
            {
                await admin.CreateTopicsAsync(
                [
                    new TopicSpecification { Name = Topic, NumPartitions = 3, ReplicationFactor = 1 },
                    new TopicSpecification { Name = DeadLetterTopic, NumPartitions = 1, ReplicationFactor = 1 },
                ], new CreateTopicsOptions { RequestTimeout = TimeSpan.FromSeconds(10) });
            }
            catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code is ErrorCode.NoError or ErrorCode.TopicAlreadyExists))
            {
            }
            Ensured.Add(bootstrapServers);
        }
        finally
        {
            Gate.Release();
        }
    }
}

/// <summary>
/// Writes an event in the caller's transaction. It is committed or rolled back together with the
/// business change, so an event can never exist without its cause, or the other way round.
/// </summary>
public interface IOutbox
{
    /// <param name="key">Kafka message key: events with the same key keep their order.</param>
    Task AddAsync<T>(string key, T message, CancellationToken ct) where T : notnull;
}

internal sealed class Outbox(SharedDbContext db) : IOutbox
{
    public Task AddAsync<T>(string key, T message, CancellationToken ct) where T : notnull
    {
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = typeof(T).Name,
            Key = key,
            Payload = JsonSerializer.Serialize(message),
            OccurredAt = DateTime.UtcNow,
        });
        return db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Moves outbox rows to Kafka, one row per transaction, in every pod. READPAST makes pods skip a row
/// another pod is publishing. A row is marked processed only after the broker acknowledged it, so a
/// crash in between republishes it (at-least-once); consumers de-duplicate by message id.
/// Publishing is retried forever: when Kafka is down the events simply wait in the table.
/// </summary>
public sealed class OutboxRelay(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<OutboxRelay> logger) : BackgroundService
{
    private readonly string _bootstrapServers = EventBus.BootstrapServers(configuration);
    private readonly Lazy<IProducer<string, string>> _producer = new(() =>
        new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = EventBus.BootstrapServers(configuration),
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = configuration.GetValue("Kafka:MessageTimeoutMs", 5000),
        }).SetLogHandler((_, _) => { }).Build());

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("BackgroundJobs:Enabled", true)) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await RunOnceAsync(stoppingToken)) { }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox relay failed; retrying on the next interval");
            }
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    /// <summary>Publishes one pending row. Returns false when nothing was published (none pending, or Kafka refused it).</summary>
    public async Task<bool> RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SharedDbContext>();

        return await scope.ServiceProvider.GetRequiredService<UnitOfWork>().ExecuteAsync(async c =>
        {
            // Rows that already failed go to the back of the queue so one bad row cannot starve the rest.
            var row = (await db.OutboxMessages
                .FromSqlInterpolated($"""
                    SELECT TOP 1 * FROM shared.OutboxMessages WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE ProcessedAt IS NULL ORDER BY Attempts, OccurredAt
                    """)
                .AsNoTracking().ToListAsync(c)).FirstOrDefault();
            if (row is null) return false;

            try
            {
                await EventBus.EnsureTopicsAsync(_bootstrapServers);
                await _producer.Value.ProduceAsync(EventBus.Topic, new Message<string, string>
                {
                    Key = row.Key,
                    Value = row.Payload,
                    Headers = new Headers
                    {
                        { EventBus.MessageIdHeader, Encoding.UTF8.GetBytes(row.Id.ToString()) },
                        { EventBus.TypeHeader, Encoding.UTF8.GetBytes(row.Type) },
                    },
                }, c);
            }
            catch (KafkaException ex)
            {
                var error = ex.Error.Reason.Length > 2000 ? ex.Error.Reason[..2000] : ex.Error.Reason;
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE shared.OutboxMessages SET Attempts = Attempts + 1, LastError = {error} WHERE Id = {row.Id}", c);
                var level = row.Attempts + 1 >= 10 ? LogLevel.Error : LogLevel.Warning;
                logger.Log(level, "Publishing outbox message {Id} failed (attempt {Attempt}): {Error}", row.Id, row.Attempts + 1, error);
                return false;
            }

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE shared.OutboxMessages SET ProcessedAt = SYSUTCDATETIME() WHERE Id = {row.Id}", c);
            return true;
        }, ct);
    }

    public override void Dispose()
    {
        if (_producer.IsValueCreated) _producer.Value.Dispose();
        base.Dispose();
    }
}
