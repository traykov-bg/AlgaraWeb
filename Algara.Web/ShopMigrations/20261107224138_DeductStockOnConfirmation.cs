using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.ShopMigrations
{
    /// <inheritdoc />
    public partial class DeductStockOnConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeductedQuantity",
                table: "OrderItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("EXEC(N'UPDATE i SET DeductedQuantity=i.Quantity FROM OrderItems i INNER JOIN Orders o ON o.N=i.OrderN WHERE o.SubmissionId IS NOT NULL AND o.Status IN (2,3);');");
            migrationBuilder.Sql("EXEC(N'ALTER TABLE [OrderItems] ADD CONSTRAINT [CK_OrderItems_DeductedQuantity] CHECK ([DeductedQuantity] >= 0 AND [DeductedQuantity] <= [Quantity]);');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM OrderItems WHERE DeductedQuantity <> 0) THROW 51000, 'Cannot discard stock deduction accounting.', 1;");
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderItems_DeductedQuantity",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "DeductedQuantity",
                table: "OrderItems");
        }
    }
}
