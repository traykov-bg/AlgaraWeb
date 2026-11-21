using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Algara.Identity.Data;
namespace Algara.Web.Migrations;
[DbContext(typeof(IdentityDbContext))]
[Migration("20261112000100_AddPaymentPermissions")]
public sealed class AddPaymentPermissions : Migration
{
 protected override void Up(MigrationBuilder m) => m.Sql("""
 INSERT INTO Permissions ([Key], Module, Name, AdminOnly) VALUES
 (N'Payments.View',N'Плащания',N'Преглед',0),
 (N'Payments.Record',N'Плащания',N'Отчитане',0),
 (N'Payments.Correct',N'Плащания',N'Корекции и възстановявания',0),
 (N'Payments.Override',N'Плащания',N'Изключения от капаро и пълно плащане',0);
 """);
 protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Cannot discard payment grants.");
}
