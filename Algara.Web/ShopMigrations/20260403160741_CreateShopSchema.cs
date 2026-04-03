using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.ShopMigrations
{
    /// <inheritdoc />
    public partial class CreateShopSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    N = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.N);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    N = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserN = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.N);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    N = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsCustomizable = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CategoryN = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.N);
                    table.ForeignKey(
                        name: "FK_Products_Categories_CategoryN",
                        column: x => x.CategoryN,
                        principalTable: "Categories",
                        principalColumn: "N",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OrderItems",
                columns: table => new
                {
                    N = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderN = table.Column<int>(type: "int", nullable: false),
                    ProductN = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => x.N);
                    table.ForeignKey(
                        name: "FK_OrderItems_Orders_OrderN",
                        column: x => x.OrderN,
                        principalTable: "Orders",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderItems_Products_ProductN",
                        column: x => x.ProductN,
                        principalTable: "Products",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderN",
                table: "OrderItems",
                column: "OrderN");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ProductN",
                table: "OrderItems",
                column: "ProductN");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_UserN",
                table: "Orders",
                column: "UserN");

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryN",
                table: "Products",
                column: "CategoryN");

            // FK към Users.N от IdentityDbContext — добавен ръчно, защото е cross-context.
            migrationBuilder.Sql(@"
                ALTER TABLE [Orders]
                ADD CONSTRAINT [FK_Orders_Users_UserN]
                FOREIGN KEY ([UserN]) REFERENCES [Users]([N])
                ON DELETE NO ACTION;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE [Orders] DROP CONSTRAINT [FK_Orders_Users_UserN];");

            migrationBuilder.DropTable(
                name: "OrderItems");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Categories");
        }
    }
}
