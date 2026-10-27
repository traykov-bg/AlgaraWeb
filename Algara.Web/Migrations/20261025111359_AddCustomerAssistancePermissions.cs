using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerAssistancePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Definitions only. No existing role receives customer assistance automatically.
            migrationBuilder.Sql("""
                INSERT INTO [Permissions] ([Key], [Module], [Name], [AdminOnly]) VALUES
                    (N'Users.PreviewCustomer', N'Потребители', N'Преглед като клиента', 0),
                    (N'Users.AssistCustomer', N'Потребители', N'Ограничено съдействие на клиента', 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Automatic downgrade would discard configured customer-assistance grants. Restore a verified backup instead.");
        }
    }
}
