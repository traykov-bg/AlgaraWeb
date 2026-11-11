using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.ShopMigrations
{
    /// <inheritdoc />
    public partial class AddInquiryOrderConversion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FurnitureInquiries_Status",
                table: "FurnitureInquiries");

            migrationBuilder.AddColumn<bool>(
                name: "IsStaffOnly",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "InquiryN",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InquiryOfferN",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServicesJson",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CatalogUnitPrice",
                table: "OrderItems",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExecutionComment",
                table: "OrderItems",
                type: "nvarchar(max)",
                maxLength: 6000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IndividualManufacture",
                table: "OrderItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReadyQuantity",
                table: "OrderItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "DeliveryAmount",
                table: "InquiryOffers",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinesJson",
                table: "InquiryOffers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServicesJson",
                table: "InquiryOffers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrderN",
                table: "FurnitureInquiries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                table: "FurnitureInquiries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VariantN",
                table: "FurnitureInquiries",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("EXEC(N'CREATE UNIQUE INDEX [IX_Orders_InquiryN] ON [Orders] ([InquiryN]) WHERE [InquiryN] IS NOT NULL AND [SplitFromN] IS NULL');");

            migrationBuilder.Sql("EXEC(N'CREATE INDEX [IX_Orders_InquiryOfferN] ON [Orders] ([InquiryOfferN])');");

            migrationBuilder.Sql("EXEC(N'ALTER TABLE [OrderItems] ADD CONSTRAINT [CK_OrderItems_IndividualReady] CHECK ([ReadyQuantity] >= 0 AND [ReadyQuantity] <= [Quantity] AND ([IndividualManufacture] = 1 OR [ReadyQuantity] = 0) AND ([IndividualManufacture] = 0 OR ([ReservedQuantity] = 0 AND [DeductedQuantity] = 0)))');");

            migrationBuilder.Sql("EXEC(N'CREATE UNIQUE INDEX [IX_FurnitureInquiries_OrderN] ON [FurnitureInquiries] ([OrderN]) WHERE [OrderN] IS NOT NULL');");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FurnitureInquiries_Status",
                table: "FurnitureInquiries",
                sql: "[Status] BETWEEN 0 AND 6");

            migrationBuilder.AddForeignKey(
                name: "FK_FurnitureInquiries_Orders_OrderN",
                table: "FurnitureInquiries",
                column: "OrderN",
                principalTable: "Orders",
                principalColumn: "N",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_FurnitureInquiries_InquiryN",
                table: "Orders",
                column: "InquiryN",
                principalTable: "FurnitureInquiries",
                principalColumn: "N",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_InquiryOffers_InquiryOfferN",
                table: "Orders",
                column: "InquiryOfferN",
                principalTable: "InquiryOffers",
                principalColumn: "N",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM Products WHERE IsStaffOnly=1)
                  OR EXISTS (SELECT 1 FROM Orders WHERE InquiryN IS NOT NULL OR InquiryOfferN IS NOT NULL OR ServicesJson IS NOT NULL)
                  OR EXISTS (SELECT 1 FROM OrderItems WHERE IndividualManufacture=1 OR ReadyQuantity<>0 OR CatalogUnitPrice IS NOT NULL OR ExecutionComment IS NOT NULL)
                  OR EXISTS (SELECT 1 FROM InquiryOffers WHERE LinesJson IS NOT NULL OR ServicesJson IS NOT NULL OR DeliveryAmount IS NOT NULL)
                  OR EXISTS (SELECT 1 FROM FurnitureInquiries WHERE OrderN IS NOT NULL OR VariantN IS NOT NULL OR Quantity IS NOT NULL OR Status=6)
                THROW 51004, 'Cannot remove inquiry order agreement data.', 1;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_FurnitureInquiries_Orders_OrderN",
                table: "FurnitureInquiries");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_FurnitureInquiries_InquiryN",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_InquiryOffers_InquiryOfferN",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_InquiryN",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_InquiryOfferN",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderItems_IndividualReady",
                table: "OrderItems");

            migrationBuilder.DropIndex(
                name: "IX_FurnitureInquiries_OrderN",
                table: "FurnitureInquiries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FurnitureInquiries_Status",
                table: "FurnitureInquiries");

            migrationBuilder.DropColumn(
                name: "IsStaffOnly",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "InquiryN",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InquiryOfferN",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ServicesJson",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CatalogUnitPrice",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "ExecutionComment",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "IndividualManufacture",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "ReadyQuantity",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "DeliveryAmount",
                table: "InquiryOffers");

            migrationBuilder.DropColumn(
                name: "LinesJson",
                table: "InquiryOffers");

            migrationBuilder.DropColumn(
                name: "ServicesJson",
                table: "InquiryOffers");

            migrationBuilder.DropColumn(
                name: "OrderN",
                table: "FurnitureInquiries");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "FurnitureInquiries");

            migrationBuilder.DropColumn(
                name: "VariantN",
                table: "FurnitureInquiries");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FurnitureInquiries_Status",
                table: "FurnitureInquiries",
                sql: "[Status] BETWEEN 0 AND 5");
        }
    }
}
