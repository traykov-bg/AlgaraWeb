using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Algara.Web.ShopMigrations
{
    /// <inheritdoc />
    public partial class AddOrderPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeferredPaymentDays",
                table: "Partners",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "DepositExempt",
                table: "Partners",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DeferredPaymentDays",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DeliveredOn",
                table: "Orders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DepositExempt",
                table: "Orders",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DepositPercent",
                table: "Orders",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DepositRequired",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PaymentDueOn",
                table: "Orders",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentMethods",
                columns: table => new
                {
                    N = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EnglishName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMethods", x => x.N);
                });

            migrationBuilder.CreateTable(
                name: "PaymentPolicies",
                columns: table => new
                {
                    N = table.Column<int>(type: "int", nullable: false),
                    DepositPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentPolicies", x => x.N);
                    table.CheckConstraint("CK_PaymentPolicies_Valid", "[N] = 1 AND [DepositPercent] > 0 AND [DepositPercent] <= 100");
                });

            migrationBuilder.CreateTable(
                name: "OrderPayments",
                columns: table => new
                {
                    N = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderN = table.Column<int>(type: "int", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MethodN = table.Column<int>(type: "int", nullable: true),
                    MethodName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReversesN = table.Column<long>(type: "bigint", nullable: true),
                    RelatedOrderN = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderPayments", x => x.N);
                    table.CheckConstraint("CK_OrderPayments_Amount", "[Amount] <> 0 AND (([Kind] = 0 AND [Amount] > 0 AND [MethodN] IS NOT NULL AND [ReversesN] IS NULL) OR ([Kind] = 1 AND [ReversesN] IS NOT NULL) OR ([Kind] = 2 AND [Amount] < 0 AND [ReversesN] IS NOT NULL) OR ([Kind] = 3 AND [RelatedOrderN] IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_OrderPayments_OrderPayments_ReversesN",
                        column: x => x.ReversesN,
                        principalTable: "OrderPayments",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderPayments_Orders_OrderN",
                        column: x => x.OrderN,
                        principalTable: "Orders",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderPayments_Orders_RelatedOrderN",
                        column: x => x.RelatedOrderN,
                        principalTable: "Orders",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderPayments_PaymentMethods_MethodN",
                        column: x => x.MethodN,
                        principalTable: "PaymentMethods",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "PaymentMethods",
                columns: new[] { "N", "EnglishName", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1, "Cash", true, "В брой" },
                    { 2, "Bank transfer", true, "Банков превод" },
                    { 3, "POS card", true, "ПОС" },
                    { 4, "Cash on delivery", true, "Наложен платеж" }
                });

            migrationBuilder.InsertData(
                table: "PaymentPolicies",
                columns: new[] { "N", "DepositPercent" },
                values: new object[] { 1, 50m });

            migrationBuilder.Sql("EXEC(N'ALTER TABLE [Partners] ADD CONSTRAINT [CK_Partners_PaymentDays] CHECK ([DeferredPaymentDays] BETWEEN 0 AND 3650)');");

            migrationBuilder.Sql("EXEC(N'ALTER TABLE [Orders] ADD CONSTRAINT [CK_Orders_DepositPercent] CHECK ([DepositPercent] IS NULL OR [DepositPercent] BETWEEN 0 AND 100)');");

            migrationBuilder.Sql("EXEC(N'ALTER TABLE [Orders] ADD CONSTRAINT [CK_Orders_PaymentDays] CHECK ([DeferredPaymentDays] IS NULL OR [DeferredPaymentDays] BETWEEN 0 AND 3650)');");

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_MethodN",
                table: "OrderPayments",
                column: "MethodN");

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_OrderN_N",
                table: "OrderPayments",
                columns: new[] { "OrderN", "N" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_RelatedOrderN",
                table: "OrderPayments",
                column: "RelatedOrderN");

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_ReversesN",
                table: "OrderPayments",
                column: "ReversesN");

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_SubmissionId",
                table: "OrderPayments",
                column: "SubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethods_Name",
                table: "PaymentMethods",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM OrderPayments) OR EXISTS (SELECT 1 FROM Orders WHERE DepositPercent IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM Partners WHERE DepositExempt=1 OR DeferredPaymentDays<>0)
                   OR EXISTS (SELECT 1 FROM PaymentPolicies WHERE DepositPercent<>50)
                   OR EXISTS (SELECT 1 FROM PaymentMethods WHERE N>4 OR IsActive=0)
                THROW 51004,'Cannot discard payment data or configured terms.',1;
                """);
            migrationBuilder.DropTable(
                name: "OrderPayments");

            migrationBuilder.DropTable(
                name: "PaymentPolicies");

            migrationBuilder.DropTable(
                name: "PaymentMethods");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Partners_PaymentDays",
                table: "Partners");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_DepositPercent",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_PaymentDays",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DeferredPaymentDays",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "DepositExempt",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "DeferredPaymentDays",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DeliveredOn",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DepositExempt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DepositPercent",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DepositRequired",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentDueOn",
                table: "Orders");
        }
    }
}
