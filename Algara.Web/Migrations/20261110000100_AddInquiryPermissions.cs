using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Algara.Identity.Data;
namespace Algara.Web.Migrations;
[DbContext(typeof(IdentityDbContext))]
[Migration("20261110000100_AddInquiryPermissions")]
public sealed class AddInquiryPermissions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        INSERT INTO Permissions ([Key], Module, Name, AdminOnly) VALUES
        (N'Inquiries.View', N'Запитвания', N'Преглед', 0),
        (N'Inquiries.Create', N'Запитвания', N'Служебно създаване', 0),
        (N'Inquiries.Manage', N'Запитвания', N'Обработка и оферти', 0);
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Cannot discard configured inquiry grants.");
}
