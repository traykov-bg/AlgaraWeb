using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.ShopMigrations
{
    /// <inheritdoc />
    public partial class AddCustomerAssistanceSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerAssistanceSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorSessionN = table.Column<int>(type: "int", nullable: false),
                    UserN = table.Column<int>(type: "int", nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    TargetSecurityVersion = table.Column<string>(type: "nvarchar(44)", maxLength: 44, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndReason = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAssistanceSessions", x => x.Id);
                    table.CheckConstraint("CK_CustomerAssistanceSessions_Duration", "[ExpiresAtUtc] = DATEADD(minute, 30, [StartedAtUtc])");
                    table.CheckConstraint("CK_CustomerAssistanceSessions_End", "([EndedAtUtc] IS NULL AND [EndReason] IS NULL) OR ([EndedAtUtc] IS NOT NULL AND [EndedAtUtc] >= [StartedAtUtc] AND [EndedAtUtc] <= [ExpiresAtUtc] AND [EndReason] IS NOT NULL AND [EndReason] IN (N'Expired', N'StaffEnded', N'InvalidContext', N'AccessChanged', N'TargetChanged'))");
                    table.CheckConstraint("CK_CustomerAssistanceSessions_Mode", "[Mode] IN (1, 2)");
                    table.CheckConstraint("CK_CustomerAssistanceSessions_Reason", "LEN(LTRIM(RTRIM([Reason]))) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAssistanceSessions_ActorId_ActorSessionN_ExpiresAtUtc",
                table: "CustomerAssistanceSessions",
                columns: new[] { "ActorId", "ActorSessionN", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAssistanceSessions_UserN_StartedAtUtc",
                table: "CustomerAssistanceSessions",
                columns: new[] { "UserN", "StartedAtUtc" });

            // Identity owns these tables. Keep the support context and its history independent
            // from customer authentication and prohibit cascaded loss of its attribution.
            migrationBuilder.AddForeignKey(
                name: "FK_CustomerAssistanceSessions_Users_UserN",
                table: "CustomerAssistanceSessions", column: "UserN",
                principalTable: "Users", principalColumn: "N", onDelete: ReferentialAction.Restrict);
            // ActorId is attribution metadata, not a new constraint on legacy Users.Id.
            // The actor's original login row supplies the identity relationship below;
            // the service checks its owner against ActorId under the shared lock.
            migrationBuilder.AddForeignKey(
                name: "FK_CustomerAssistanceSessions_UserSessions_ActorSessionN",
                table: "CustomerAssistanceSessions", column: "ActorSessionN",
                principalTable: "UserSessions", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Automatic downgrade would discard customer-assistance context and lifecycle history. Restore a verified backup instead.");
        }
    }
}
