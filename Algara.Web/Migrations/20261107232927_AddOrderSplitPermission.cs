using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Algara.Identity.Data;
namespace Algara.Web.Migrations;
[DbContext(typeof(IdentityDbContext))]
[Migration("20261107232927_AddOrderSplitPermission")]
public sealed class AddOrderSplitPermission : Migration
{
 protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("INSERT INTO Permissions ([Key], Module, Name, AdminOnly) VALUES (N'Orders.Split', N'Поръчки', N'Разделяне на поръчки', 0);");
 protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Cannot discard configured order split grants.");
}
