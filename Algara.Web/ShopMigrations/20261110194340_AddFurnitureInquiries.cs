using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.ShopMigrations
{
    /// <inheritdoc />
    public partial class AddFurnitureInquiries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FurnitureInquiries",
                columns: table => new
                {
                    N = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GuestTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CustomerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedByStaffId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AssignedStaffId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProductN = table.Column<int>(type: "int", nullable: true),
                    ProductName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ContactName = table.Column<string>(type: "nvarchar(121)", maxLength: 121, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Dimensions = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Materials = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FurnitureInquiries", x => x.N);
                    table.CheckConstraint("CK_FurnitureInquiries_Status", "[Status] BETWEEN 0 AND 5");
                    table.ForeignKey(
                        name: "FK_FurnitureInquiries_Products_ProductN",
                        column: x => x.ProductN,
                        principalTable: "Products",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InquiryAttachments",
                columns: table => new
                {
                    N = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InquiryN = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Contents = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InquiryAttachments", x => x.N);
                    table.CheckConstraint("CK_InquiryAttachments_Size", "DATALENGTH([Contents]) BETWEEN 1 AND 5242880");
                    table.ForeignKey(
                        name: "FK_InquiryAttachments_FurnitureInquiries_InquiryN",
                        column: x => x.InquiryN,
                        principalTable: "FurnitureInquiries",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InquiryEvents",
                columns: table => new
                {
                    N = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InquiryN = table.Column<int>(type: "int", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InquiryEvents", x => x.N);
                    table.ForeignKey(
                        name: "FK_InquiryEvents_FurnitureInquiries_InquiryN",
                        column: x => x.InquiryN,
                        principalTable: "FurnitureInquiries",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InquiryOffers",
                columns: table => new
                {
                    N = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InquiryN = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Terms = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InquiryOffers", x => x.N);
                    table.CheckConstraint("CK_InquiryOffers_Price", "[Price] > 0 AND [Price] <= 999999999");
                    table.ForeignKey(
                        name: "FK_InquiryOffers_FurnitureInquiries_InquiryN",
                        column: x => x.InquiryN,
                        principalTable: "FurnitureInquiries",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FurnitureInquiries_CustomerId_CreatedAtUtc",
                table: "FurnitureInquiries",
                columns: new[] { "CustomerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FurnitureInquiries_ProductN",
                table: "FurnitureInquiries",
                column: "ProductN");

            migrationBuilder.CreateIndex(
                name: "IX_FurnitureInquiries_Status_CreatedAtUtc",
                table: "FurnitureInquiries",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FurnitureInquiries_SubmissionId",
                table: "FurnitureInquiries",
                column: "SubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InquiryAttachments_InquiryN",
                table: "InquiryAttachments",
                column: "InquiryN");

            migrationBuilder.CreateIndex(
                name: "IX_InquiryEvents_InquiryN",
                table: "InquiryEvents",
                column: "InquiryN");

            migrationBuilder.CreateIndex(
                name: "IX_InquiryOffers_InquiryN",
                table: "InquiryOffers",
                column: "InquiryN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM FurnitureInquiries) THROW 51004, 'Cannot discard inquiry history and private attachments.', 1;");
            migrationBuilder.DropTable(
                name: "InquiryAttachments");

            migrationBuilder.DropTable(
                name: "InquiryEvents");

            migrationBuilder.DropTable(
                name: "InquiryOffers");

            migrationBuilder.DropTable(
                name: "FurnitureInquiries");
        }
    }
}
