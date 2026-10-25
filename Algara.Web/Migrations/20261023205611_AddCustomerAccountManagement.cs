using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerAccountManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AccessSuspendedAtUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            // SQL Server compiles a scripted batch before its new column exists.
            migrationBuilder.Sql("""
                EXEC(N'ALTER TABLE [Users] ADD CONSTRAINT [CK_Users_AccessSuspendedInactive]
                    CHECK ([AccessSuspendedAtUtc] IS NULL OR [IsActive] = 0);');
                """);

            // Definitions only: existing accounts, role assignments and grants are preserved.
            migrationBuilder.Sql("""
                INSERT INTO [Permissions] ([Key], [Module], [Name], [AdminOnly]) VALUES
                    (N'Users.EditProfile', N'Потребители', N'Редакция на имена и телефон', 0),
                    (N'Users.ManageAddresses', N'Потребители', N'Управление на адреси', 0),
                    (N'Users.Unlock', N'Потребители', N'Отключване след неуспешни входове', 0),
                    (N'Users.SuspendAccess', N'Потребители', N'Временно спиране и възстановяване на достъпа', 0),
                    (N'Users.ViewSessions', N'Потребители', N'Преглед на сесии', 0),
                    (N'Users.RevokeSessions', N'Потребители', N'Прекратяване на сесии', 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Automatic downgrade would remove suspension state and configured customer-management grants. Restore a verified backup instead.");
        }
    }
}
