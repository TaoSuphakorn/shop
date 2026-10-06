using Microsoft.EntityFrameworkCore;
using Shop.Shared;

namespace Shop.Ordering;

public class Order
{
    public const string Paid = "Paid", Cancelled = "Cancelled";

    public Guid Id { get; set; }
    public Guid GuestId { get; set; }
    public string Status { get; set; } = Paid;
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}

public class OrderItem
{
    public Guid OrderId { get; set; }
    public int ProductId { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

public class OrderingDbContext(DbContextOptions<OrderingDbContext> options, UnitOfWork unitOfWork) : ModuleDbContext(options, unitOfWork)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("ordering");
        model.Entity<Order>(e =>
        {
            e.ToTable("Orders", t => t.HasCheckConstraint("CK_Orders_Status", "[Status] IN ('Paid', 'Cancelled')"));
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.GuestId);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Total).HasPrecision(18, 2);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.OrderId);
        });
        model.Entity<OrderItem>(e =>
        {
            e.ToTable("OrderItems");
            e.HasKey(x => new { x.OrderId, x.ProductId });
            e.Property(x => x.Sku).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);
        });
    }
}
