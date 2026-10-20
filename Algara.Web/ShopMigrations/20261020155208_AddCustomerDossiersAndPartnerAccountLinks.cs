using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.ShopMigrations
{
    /// <inheritdoc />
    public partial class AddCustomerDossiersAndPartnerAccountLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowAccountLinks",
                table: "Partners",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CustomerDossierEvents",
                columns: table => new
                {
                    N = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserN = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    VerificationMethod = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerDossierEvents", x => x.N);
                });

            migrationBuilder.CreateTable(
                name: "PartnerAccountLinks",
                columns: table => new
                {
                    UserN = table.Column<int>(type: "int", nullable: false),
                    PartnerN = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartnerAccountLinks", x => x.UserN);
                    table.ForeignKey(
                        name: "FK_PartnerAccountLinks_Partners_PartnerN",
                        column: x => x.PartnerN,
                        principalTable: "Partners",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDossierEvents_UserN_OccurredAtUtc_N",
                table: "CustomerDossierEvents",
                columns: new[] { "UserN", "OccurredAtUtc", "N" });

            migrationBuilder.CreateIndex(
                name: "IX_PartnerAccountLinks_PartnerN",
                table: "PartnerAccountLinks",
                column: "PartnerN");

            // Users belongs to IdentityDbContext. Keep these cross-context references in SQL,
            // with no cascade that could erase a membership or the customer audit history.
            migrationBuilder.AddForeignKey(
                name: "FK_PartnerAccountLinks_Users_UserN",
                table: "PartnerAccountLinks",
                column: "UserN",
                principalTable: "Users",
                principalColumn: "N",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerDossierEvents_Users_UserN",
                table: "CustomerDossierEvents",
                column: "UserN",
                principalTable: "Users",
                principalColumn: "N",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A downgrade would discard verified account membership and its audit trail.
            throw new NotSupportedException("Връщането на стария модел изисква възстановяване от резервно копие преди тази миграция.");
        }
    }
}
