using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountEmailFlows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Explicit opt-in for staff: preserve every existing role assignment and grant.
            migrationBuilder.Sql("""
                INSERT INTO [Permissions] ([Key], [Module], [Name], [AdminOnly]) VALUES
                    (N'Users.SendAccountEmails', N'Потребители', N'Изпращане на имейли за достъп', 0),
                    (N'Users.ManageInvitations', N'Потребители', N'Покани за клиентски акаунт', 0);
                """);
            migrationBuilder.AddColumn<bool>(
                name: "EmailConfirmed",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailConfirmedAtUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequireEmailConfirmation",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PublicSiteUrl",
                table: "SmtpConfigurations",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AccountEmailAuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserN = table.Column<int>(type: "int", nullable: true),
                    PartnerN = table.Column<int>(type: "int", nullable: true),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountEmailAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountEmailTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    UserN = table.Column<int>(type: "int", nullable: true),
                    PartnerN = table.Column<int>(type: "int", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TokenHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ExpectedSecurityStamp = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedById = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    VerificationMethod = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PartnerVersion = table.Column<byte[]>(type: "varbinary(8)", maxLength: 8, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountEmailTokens", x => x.Id);
                    table.CheckConstraint("CK_AccountEmailTokens_Purpose", "[Purpose] IN (1,2,3)");
                    table.CheckConstraint("CK_AccountEmailTokens_Target", "([Purpose] IN (1,2) AND [UserN] IS NOT NULL AND [PartnerN] IS NULL AND [ExpectedSecurityStamp] IS NOT NULL) OR ([Purpose]=3 AND [PartnerN] IS NOT NULL AND [PartnerN]>0 AND [CreatedById] IS NOT NULL AND [PartnerVersion] IS NOT NULL AND DATALENGTH([PartnerVersion])=8)");
                    table.CheckConstraint("CK_AccountEmailTokens_Terminal", "[ConsumedAtUtc] IS NULL OR [RevokedAtUtc] IS NULL");
                    table.ForeignKey(
                        name: "FK_AccountEmailTokens_Users_UserN",
                        column: x => x.UserN,
                        principalTable: "Users",
                        principalColumn: "N");
                });

            migrationBuilder.CreateTable(
                name: "EmailOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProtectedPayload = table.Column<string>(type: "nvarchar(max)", maxLength: 32768, nullable: true),
                    TokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailOutboxMessages", x => x.Id);
                    table.CheckConstraint("CK_EmailOutboxMessages_Attempts", "[Attempts] BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_EmailOutboxMessages_Lease", "([State] = 'Processing' AND [LeaseId] IS NOT NULL AND [LeaseUntilUtc] IS NOT NULL) OR ([State] <> 'Processing' AND [LeaseId] IS NULL AND [LeaseUntilUtc] IS NULL)");
                    table.CheckConstraint("CK_EmailOutboxMessages_Payload", "([State] IN ('Pending','Processing') AND [ProtectedPayload] IS NOT NULL) OR ([State] IN ('Accepted','Failed','Cancelled') AND [ProtectedPayload] IS NULL)");
                    table.CheckConstraint("CK_EmailOutboxMessages_State", "[State] IN ('Pending','Processing','Accepted','Failed','Cancelled')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmailAuditEvents_PartnerN_OccurredAtUtc",
                table: "AccountEmailAuditEvents",
                columns: new[] { "PartnerN", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmailAuditEvents_UserN_OccurredAtUtc",
                table: "AccountEmailAuditEvents",
                columns: new[] { "UserN", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmailTokens_Email_Purpose_CreatedAtUtc",
                table: "AccountEmailTokens",
                columns: new[] { "Email", "Purpose", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmailTokens_PartnerN_CreatedAtUtc",
                table: "AccountEmailTokens",
                columns: new[] { "PartnerN", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmailTokens_TokenHash",
                table: "AccountEmailTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmailTokens_UserN_CreatedAtUtc",
                table: "AccountEmailTokens",
                columns: new[] { "UserN", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutboxMessages_State_NextAttemptAtUtc_LeaseUntilUtc",
                table: "EmailOutboxMessages",
                columns: new[] { "State", "NextAttemptAtUtc", "LeaseUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutboxMessages_TokenId",
                table: "EmailOutboxMessages",
                column: "TokenId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Automatic downgrade would remove account verification, access tokens and email history. Restore a verified backup instead.");
        }
    }
}
