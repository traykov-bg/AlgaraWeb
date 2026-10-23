using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddSmtpConfigurationAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SmtpConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Host = table.Column<string>(type: "nvarchar(253)", maxLength: 253, nullable: false),
                    Port = table.Column<int>(type: "int", nullable: false),
                    SecurityMode = table.Column<int>(type: "int", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FromEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FromName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    ProtectedPassword = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmtpConfigurations", x => x.Id);
                    table.CheckConstraint("CK_SmtpConfigurations_EnabledPassword", "[Enabled] = 0 OR ([ProtectedPassword] IS NOT NULL AND LEN([ProtectedPassword]) > 0)");
                    table.CheckConstraint("CK_SmtpConfigurations_Port", "[Port] BETWEEN 1 AND 65535");
                    table.CheckConstraint("CK_SmtpConfigurations_SecurePort", "([Port] <> 465 OR [SecurityMode] = 2) AND ([Port] <> 587 OR [SecurityMode] = 1)");
                    table.CheckConstraint("CK_SmtpConfigurations_SecurityMode", "[SecurityMode] IN (1, 2)");
                    table.CheckConstraint("CK_SmtpConfigurations_Singleton", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "SmtpSettingsAuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmtpSettingsAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SmtpSettingsAuditEvents_OccurredAtUtc",
                table: "SmtpSettingsAuditEvents",
                column: "OccurredAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Automatic downgrade would remove SMTP settings and their audit history. Restore a verified backup instead.");
        }
    }
}
