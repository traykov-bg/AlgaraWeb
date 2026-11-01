using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.ShopMigrations
{
    /// <inheritdoc />
    public partial class AddProductShippingParcels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowsCourier",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "UseCatalogDimensionsForShipping",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ProductShippingParcels",
                columns: table => new
                {
                    N = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductN = table.Column<int>(type: "int", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    WeightKg = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    WidthCm = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    HeightCm = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    DepthCm = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductShippingParcels", x => x.N);
                    table.CheckConstraint("CK_ProductShippingParcels_Dimensions", "([WidthCm] IS NULL AND [HeightCm] IS NULL AND [DepthCm] IS NULL) OR ([WidthCm] IS NOT NULL AND [HeightCm] IS NOT NULL AND [DepthCm] IS NOT NULL AND [WidthCm] >= 0.01 AND [WidthCm] <= 100000 AND [HeightCm] >= 0.01 AND [HeightCm] <= 100000 AND [DepthCm] >= 0.01 AND [DepthCm] <= 100000)");
                    table.CheckConstraint("CK_ProductShippingParcels_Position", "[Position] >= 1 AND [Position] <= 50");
                    table.CheckConstraint("CK_ProductShippingParcels_Weight", "[WeightKg] >= 0.001 AND [WeightKg] <= 100000");
                    table.ForeignKey(
                        name: "FK_ProductShippingParcels_Products_ProductN",
                        column: x => x.ProductN,
                        principalTable: "Products",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductShippingParcels_ProductN_Position",
                table: "ProductShippingParcels",
                columns: new[] { "ProductN", "Position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Automatic downgrade would discard product shipping data. Restore a verified backup instead.");
        }
    }
}
