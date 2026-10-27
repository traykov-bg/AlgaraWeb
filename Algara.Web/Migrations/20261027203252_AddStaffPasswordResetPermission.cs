using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffPasswordResetPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Definition only; Moderator access requires an explicit grant from an administrator.
            migrationBuilder.Sql("""
                INSERT INTO [Permissions] ([Key], [Module], [Name], [AdminOnly]) VALUES
                    (N'Users.ResetPassword', N'Потребители', N'Задаване на нова парола без имейл', 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Automatic downgrade would discard configured password-reset grants. Restore a verified backup instead.");
        }
    }
}
