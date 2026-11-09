using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Algara.Identity.Data;
namespace Algara.Web.Migrations;
[DbContext(typeof(IdentityDbContext))]
[Migration("20261109000100_AddStaffOrderPermission")]
public sealed class AddStaffOrderPermission : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("INSERT INTO Permissions ([Key], Module, Name, AdminOnly) VALUES (N'Orders.Create', N'Поръчки', N'Служебно създаване', 0);");
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Cannot discard configured staff order grants.");
}
