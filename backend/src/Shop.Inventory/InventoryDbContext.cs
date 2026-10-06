using Microsoft.EntityFrameworkCore;
using Shop.Shared;

namespace Shop.Inventory;

public class Stock
{
    public int ProductId { get; set; }
    public int OnHand { get; set; }
    public int Reserved { get; set; }
}

public class InventoryDbContext(DbContextOptions<InventoryDbContext> options, UnitOfWork unitOfWork) : ModuleDbContext(options, unitOfWork)
{
    // Mock stock, one row per seeded product (spec 6): ProductId -> OnHand.
    private static readonly int[] SeedOnHand = [25, 12, 40, 3, 8, 60, 1, 30, 100, 5, 0, 2];

    public DbSet<Stock> Stock => Set<Stock>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("inventory");
        model.Entity<Stock>(e =>
        {
            e.ToTable("Stock", t =>
            {
                // Last line of defence if application code is ever wrong.
                t.HasCheckConstraint("CK_Stock_OnHand", "[OnHand] >= 0");
                t.HasCheckConstraint("CK_Stock_Reserved", "[Reserved] >= 0");
                t.HasCheckConstraint("CK_Stock_ReservedWithinOnHand", "[Reserved] <= [OnHand]");
            });
            e.HasKey(x => x.ProductId);
            e.Property(x => x.ProductId).ValueGeneratedNever();
            e.HasData(SeedOnHand.Select((onHand, i) => new Stock { ProductId = i + 1, OnHand = onHand }));
        });
    }
}
