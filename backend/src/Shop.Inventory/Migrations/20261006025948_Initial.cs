using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Shop.Inventory.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateTable(
                name: "Stock",
                schema: "inventory",
                columns: table => new
                {
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    OnHand = table.Column<int>(type: "int", nullable: false),
                    Reserved = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stock", x => x.ProductId);
                    table.CheckConstraint("CK_Stock_OnHand", "[OnHand] >= 0");
                    table.CheckConstraint("CK_Stock_Reserved", "[Reserved] >= 0");
                    table.CheckConstraint("CK_Stock_ReservedWithinOnHand", "[Reserved] <= [OnHand]");
                });

            migrationBuilder.InsertData(
                schema: "inventory",
                table: "Stock",
                columns: new[] { "ProductId", "OnHand", "Reserved" },
                values: new object[,]
                {
                    { 1, 25, 0 },
                    { 2, 12, 0 },
                    { 3, 40, 0 },
                    { 4, 3, 0 },
                    { 5, 8, 0 },
                    { 6, 60, 0 },
                    { 7, 1, 0 },
                    { 8, 30, 0 },
                    { 9, 100, 0 },
                    { 10, 5, 0 },
                    { 11, 0, 0 },
                    { 12, 2, 0 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Stock",
                schema: "inventory");
        }
    }
}
