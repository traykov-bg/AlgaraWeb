using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.ShopMigrations
{
    /// <inheritdoc />
    public partial class AddOrderSplitting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SplitFromN",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Details",
                table: "OrderEvents",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_SplitFromN",
                table: "Orders",
                column: "SplitFromN");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Orders_SplitFromN",
                table: "Orders",
                column: "SplitFromN",
                principalTable: "Orders",
                principalColumn: "N",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM Orders WHERE SplitFromN IS NOT NULL) OR EXISTS (SELECT 1 FROM OrderEvents WHERE Details IS NOT NULL) THROW 51000, 'Cannot discard order splitting history.', 1;");
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Orders_SplitFromN",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_SplitFromN",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SplitFromN",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Details",
                table: "OrderEvents");
        }
    }
}
