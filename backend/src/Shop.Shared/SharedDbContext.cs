using Microsoft.EntityFrameworkCore;

namespace Shop.Shared;

public class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public string Key { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTime OccurredAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

public class ProcessedMessage
{
    public Guid MessageId { get; set; }
    public string Handler { get; set; } = "";
}

public class IdempotencyKey
{
    public Guid GuestId { get; set; }
    public string Key { get; set; } = "";
    public byte[] RequestHash { get; set; } = [];
    public int StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SharedDbContext(DbContextOptions<SharedDbContext> options, UnitOfWork unitOfWork) : ModuleDbContext(options, unitOfWork)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("shared");
        model.Entity<OutboxMessage>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Type).HasMaxLength(100);
            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.LastError).HasMaxLength(2000);
            e.HasIndex(x => x.OccurredAt).HasFilter("[ProcessedAt] IS NULL");
        });
        model.Entity<ProcessedMessage>(e =>
        {
            e.HasKey(x => new { x.MessageId, x.Handler });
            e.Property(x => x.Handler).HasMaxLength(100);
        });
        model.Entity<IdempotencyKey>(e =>
        {
            e.HasKey(x => new { x.GuestId, x.Key });
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.RequestHash).HasColumnType("binary(32)");
            e.HasIndex(x => x.CreatedAt);
        });
    }
}
