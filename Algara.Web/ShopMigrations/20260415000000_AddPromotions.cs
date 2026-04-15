using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.ShopMigrations
{
    /// <inheritdoc />
    public partial class AddPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Promotions",
                columns: table => new
                {
                    N = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DiscountPercent = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promotions", x => x.N);
                });

            migrationBuilder.CreateTable(
                name: "ProductPromotions",
                columns: table => new
                {
                    ProductN = table.Column<int>(type: "int", nullable: false),
                    PromotionN = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPromotions", x => new { x.ProductN, x.PromotionN });
                    table.ForeignKey(
                        name: "FK_ProductPromotions_Products_ProductN",
                        column: x => x.ProductN,
                        principalTable: "Products",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductPromotions_Promotions_PromotionN",
                        column: x => x.PromotionN,
                        principalTable: "Promotions",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductPromotions_PromotionN",
                table: "ProductPromotions",
                column: "PromotionN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductPromotions");

            migrationBuilder.DropTable(
                name: "Promotions");
        }
    }
}
