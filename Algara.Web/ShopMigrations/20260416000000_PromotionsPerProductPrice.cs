using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.ShopMigrations
{
    /// <inheritdoc />
    public partial class PromotionsPerProductPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- Promotions: добавяне на Type и UserCreated ---
            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Promotions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UserCreated",
                table: "Promotions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            // --- ProductPromotions: нови колони за per-product цени ---
            migrationBuilder.AddColumn<decimal>(
                name: "OriginalPrice",
                table: "ProductPromotions",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PromoPrice",
                table: "ProductPromotions",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercent",
                table: "ProductPromotions",
                type: "decimal(5,3)",
                nullable: false,
                defaultValue: 0m);

            // --- Backfill: за съществуващи записи изчислява OriginalPrice/DiscountPercent/PromoPrice
            //     от текущата Product.Price и Promotion.DiscountPercent.
            migrationBuilder.Sql(@"
UPDATE pp
   SET pp.OriginalPrice   = p.Price,
       pp.DiscountPercent = pr.DiscountPercent,
       pp.PromoPrice      = CAST(ROUND(p.Price * (1 - CAST(pr.DiscountPercent AS decimal(18,6)) / 100.0), 2) AS decimal(18,2))
  FROM ProductPromotions pp
  JOIN Products   p  ON p.N  = pp.ProductN
  JOIN Promotions pr ON pr.N = pp.PromotionN;
");

            // --- Promotions: премахване на вече ненужната DiscountPercent ---
            migrationBuilder.DropColumn(
                name: "DiscountPercent",
                table: "Promotions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Възстановяване на DiscountPercent на Promotions (не попълва обратно старите стойности).
            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercent",
                table: "Promotions",
                type: "decimal(5,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.DropColumn(
                name: "DiscountPercent",
                table: "ProductPromotions");

            migrationBuilder.DropColumn(
                name: "PromoPrice",
                table: "ProductPromotions");

            migrationBuilder.DropColumn(
                name: "OriginalPrice",
                table: "ProductPromotions");

            migrationBuilder.DropColumn(
                name: "UserCreated",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Promotions");
        }
    }
}
