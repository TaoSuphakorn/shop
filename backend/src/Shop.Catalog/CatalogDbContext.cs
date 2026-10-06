using Microsoft.EntityFrameworkCore;
using Shop.Shared;

namespace Shop.Catalog;

public class Product
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public string ImageUrl { get; set; } = "";
}

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options, UnitOfWork unitOfWork) : ModuleDbContext(options, unitOfWork)
{
    // Mock product data (the brief allows seeding instead of an import program).
    private static readonly (string Name, string Description, decimal Price)[] Seed =
    [
        ("หูฟังไร้สาย AirBeat Pro", "ตัดเสียงรบกวน แบตเตอรี่ใช้ได้ 30 ชั่วโมง", 2990m),
        ("คีย์บอร์ด Mechanical K87", "สวิตช์เชิงกล 87 ปุ่ม ไฟ RGB", 3490m),
        ("เมาส์ไร้สาย Glide M2", "น้ำหนักเบา 59 กรัม เชื่อมต่อได้ 3 อุปกรณ์", 1290m),
        ("ลำโพงบลูทูธ BoomMini", "กันน้ำ IPX7 เสียงเบสแน่นในขนาดพกพา", 1890m),
        ("สมาร์ทวอทช์ Pulse S", "วัดชีพจรและการนอน แบตเตอรี่ 10 วัน", 4990m),
        ("พาวเวอร์แบงค์ 20000mAh", "ชาร์จเร็ว 65W ชาร์จโน้ตบุ๊กได้", 990m),
        ("เว็บแคม Full HD", "ความละเอียด 1080p พร้อมไมโครโฟนคู่", 1590m),
        ("แท่นวางโน้ตบุ๊กอะลูมิเนียม", "ปรับระดับได้ 6 ระดับ ระบายความร้อนดี", 790m),
        ("สายชาร์จ USB-C 100W", "สายถักยาว 2 เมตร รองรับชาร์จเร็ว", 390m),
        ("จอมอนิเตอร์ 27 นิ้ว 4K", "พาเนล IPS สีตรง 99% sRGB", 11900m),
        ("ไมโครโฟน USB StudioCast", "คอนเดนเซอร์สำหรับพอดแคสต์และสตรีม", 2490m),
        ("กระเป๋าโน้ตบุ๊ก 15.6 นิ้ว", "กันน้ำ กันกระแทก มีช่องเก็บอุปกรณ์", 1190m),
    ];

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("catalog");
        model.Entity<Product>(e =>
        {
            e.ToTable("Products", t =>
            {
                t.HasCheckConstraint("CK_Products_Price", "[Price] >= 0");
                // Blocks javascript: and off-site image URLs at the source (spec 11.3).
                t.HasCheckConstraint("CK_Products_ImageUrl", "[ImageUrl] LIKE '/products/%'");
            });
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Sku).HasMaxLength(32);
            e.HasIndex(x => x.Sku).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.Property(x => x.ImageUrl).HasMaxLength(300);
            e.HasData(Seed.Select((p, i) => new Product
            {
                Id = i + 1,
                Sku = $"TNC-{i + 1:000}",
                Name = p.Name,
                Description = p.Description,
                Price = p.Price,
                ImageUrl = $"/products/tnc-{i + 1:000}.svg",
            }));
        });
    }
}
