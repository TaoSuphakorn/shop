using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Shop.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "Products",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.CheckConstraint("CK_Products_ImageUrl", "[ImageUrl] LIKE '/products/%'");
                    table.CheckConstraint("CK_Products_Price", "[Price] >= 0");
                });

            migrationBuilder.InsertData(
                schema: "catalog",
                table: "Products",
                columns: new[] { "Id", "Description", "ImageUrl", "Name", "Price", "Sku" },
                values: new object[,]
                {
                    { 1, "ตัดเสียงรบกวน แบตเตอรี่ใช้ได้ 30 ชั่วโมง", "/products/tnc-001.svg", "หูฟังไร้สาย AirBeat Pro", 2990m, "TNC-001" },
                    { 2, "สวิตช์เชิงกล 87 ปุ่ม ไฟ RGB", "/products/tnc-002.svg", "คีย์บอร์ด Mechanical K87", 3490m, "TNC-002" },
                    { 3, "น้ำหนักเบา 59 กรัม เชื่อมต่อได้ 3 อุปกรณ์", "/products/tnc-003.svg", "เมาส์ไร้สาย Glide M2", 1290m, "TNC-003" },
                    { 4, "กันน้ำ IPX7 เสียงเบสแน่นในขนาดพกพา", "/products/tnc-004.svg", "ลำโพงบลูทูธ BoomMini", 1890m, "TNC-004" },
                    { 5, "วัดชีพจรและการนอน แบตเตอรี่ 10 วัน", "/products/tnc-005.svg", "สมาร์ทวอทช์ Pulse S", 4990m, "TNC-005" },
                    { 6, "ชาร์จเร็ว 65W ชาร์จโน้ตบุ๊กได้", "/products/tnc-006.svg", "พาวเวอร์แบงค์ 20000mAh", 990m, "TNC-006" },
                    { 7, "ความละเอียด 1080p พร้อมไมโครโฟนคู่", "/products/tnc-007.svg", "เว็บแคม Full HD", 1590m, "TNC-007" },
                    { 8, "ปรับระดับได้ 6 ระดับ ระบายความร้อนดี", "/products/tnc-008.svg", "แท่นวางโน้ตบุ๊กอะลูมิเนียม", 790m, "TNC-008" },
                    { 9, "สายถักยาว 2 เมตร รองรับชาร์จเร็ว", "/products/tnc-009.svg", "สายชาร์จ USB-C 100W", 390m, "TNC-009" },
                    { 10, "พาเนล IPS สีตรง 99% sRGB", "/products/tnc-010.svg", "จอมอนิเตอร์ 27 นิ้ว 4K", 11900m, "TNC-010" },
                    { 11, "คอนเดนเซอร์สำหรับพอดแคสต์และสตรีม", "/products/tnc-011.svg", "ไมโครโฟน USB StudioCast", 2490m, "TNC-011" },
                    { 12, "กันน้ำ กันกระแทก มีช่องเก็บอุปกรณ์", "/products/tnc-012.svg", "กระเป๋าโน้ตบุ๊ก 15.6 นิ้ว", 1190m, "TNC-012" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Sku",
                schema: "catalog",
                table: "Products",
                column: "Sku",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Products",
                schema: "catalog");
        }
    }
}
