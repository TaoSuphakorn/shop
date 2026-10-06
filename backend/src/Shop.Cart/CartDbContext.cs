using Microsoft.EntityFrameworkCore;
using Shop.Shared;

namespace Shop.Cart;

public class ShoppingCart
{
    public Guid GuestId { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class CartItem
{
    public Guid GuestId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

public class CartDbContext(DbContextOptions<CartDbContext> options, UnitOfWork unitOfWork) : ModuleDbContext(options, unitOfWork)
{
    public DbSet<ShoppingCart> Carts => Set<ShoppingCart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("cart");
        model.Entity<ShoppingCart>(e =>
        {
            e.ToTable("Carts");
            e.HasKey(x => x.GuestId);
            e.Property(x => x.GuestId).ValueGeneratedNever();
            e.HasIndex(x => x.ExpiresAt);
        });
        model.Entity<CartItem>(e =>
        {
            e.ToTable("CartItems", t => t.HasCheckConstraint("CK_CartItems_Quantity", "[Quantity] BETWEEN 1 AND 99"));
            e.HasKey(x => new { x.GuestId, x.ProductId });
            e.HasOne<ShoppingCart>().WithMany().HasForeignKey(x => x.GuestId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
