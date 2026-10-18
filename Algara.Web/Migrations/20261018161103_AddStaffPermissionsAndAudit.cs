using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffPermissionsAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Roles] WHERE DATALENGTH([Name]) > 512 OR LEN(LTRIM(RTRIM([Name]))) = 0)
                    THROW 51005, 'Role names must contain 1 to 256 characters. Correct existing roles before applying this migration.', 1;
                IF EXISTS (SELECT [Name] FROM [Roles] GROUP BY [Name] HAVING COUNT(*) > 1)
                    THROW 51005, 'Duplicate role names exist. Resolve them without losing memberships before applying this migration.', 1;
                IF EXISTS (
                    SELECT 1 FROM [Roles] r
                    JOIN (VALUES(N'Admin'),(N'Moderator'),(N'CatalogEditor'),(N'OrderManager'),(N'User')) canonical([Name])
                      ON r.[Name] COLLATE Latin1_General_100_CI_AS = canonical.[Name]
                    WHERE r.[Name] COLLATE Latin1_General_100_BIN2 <> canonical.[Name] COLLATE Latin1_General_100_BIN2
                       OR DATALENGTH(r.[Name]) <> DATALENGTH(canonical.[Name]))
                    THROW 51005, 'Built-in role names must use exact canonical spelling: Admin, Moderator, CatalogEditor, OrderManager, User.', 1;
                """);
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Roles",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<long>(
                name: "PermissionsVersion",
                table: "Roles",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "AccessAuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Target = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TargetName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Module = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AdminOnly = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RoleN = table.Column<int>(type: "int", nullable: false),
                    PermissionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleN, x.PermissionKey });
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions_PermissionKey",
                        column: x => x.PermissionKey,
                        principalTable: "Permissions",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleN",
                        column: x => x.RoleN,
                        principalTable: "Roles",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessAuditEvents_OccurredAtUtc",
                table: "AccessAuditEvents",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionKey",
                table: "RolePermissions",
                column: "PermissionKey");
            // Frozen initial catalog: future changes belong in a new migration.
            migrationBuilder.Sql("""
                INSERT INTO [Roles] ([Id], [Name], [Description])
                SELECT CONVERT(nvarchar(36), NEWID()), seed.[Name], seed.[Description]
                FROM (VALUES
                    (N'Admin', N'Администратор'),
                    (N'Moderator', N'Модератор'),
                    (N'CatalogEditor', N'Редактор на каталог'),
                    (N'OrderManager', N'Служител за поръчки'),
                    (N'User', N'Клиент')) seed([Name], [Description])
                WHERE NOT EXISTS (SELECT 1 FROM [Roles] r WHERE r.[Name] = seed.[Name]);

                INSERT INTO [Permissions] ([Key], [Module], [Name], [AdminOnly]) VALUES
                    (N'Dashboard.View', N'Табло', N'Преглед', 0),
                    (N'Products.View', N'Продукти', N'Преглед', 0),
                    (N'Products.Create', N'Продукти', N'Създаване', 0),
                    (N'Products.Edit', N'Продукти', N'Редакция', 0),
                    (N'Products.ChangePrice', N'Продукти', N'Промяна на цени', 0),
                    (N'Products.ChangeStock', N'Продукти', N'Промяна на наличности', 0),
                    (N'Products.Deactivate', N'Продукти', N'Активиране и деактивиране', 0),
                    (N'Options.View', N'Дамаски и цветове', N'Преглед', 0),
                    (N'Options.Create', N'Дамаски и цветове', N'Създаване', 0),
                    (N'Options.Edit', N'Дамаски и цветове', N'Редакция', 0),
                    (N'Options.Deactivate', N'Дамаски и цветове', N'Активиране и деактивиране', 0),
                    (N'Categories.View', N'Категории', N'Преглед', 0),
                    (N'Categories.Create', N'Категории', N'Създаване', 0),
                    (N'Categories.Edit', N'Категории', N'Редакция', 0),
                    (N'Categories.Deactivate', N'Категории', N'Активиране и деактивиране', 0),
                    (N'Categories.Delete', N'Категории', N'Изтриване', 0),
                    (N'Content.View', N'Съдържание', N'Преглед', 0),
                    (N'Content.Create', N'Съдържание', N'Създаване', 0),
                    (N'Content.Edit', N'Съдържание', N'Редакция', 0),
                    (N'Content.Deactivate', N'Съдържание', N'Активиране и деактивиране', 0),
                    (N'Promotions.View', N'Промоции', N'Преглед', 0),
                    (N'Promotions.Create', N'Промоции', N'Създаване', 0),
                    (N'Promotions.Edit', N'Промоции', N'Редакция', 0),
                    (N'Promotions.Deactivate', N'Промоции', N'Активиране и деактивиране', 0),
                    (N'Promotions.Delete', N'Промоции', N'Изтриване', 0),
                    (N'Orders.View', N'Поръчки', N'Преглед', 0),
                    (N'Orders.ChangeStatus', N'Поръчки', N'Промяна на статус', 0),
                    (N'Companies.View', N'Фирми', N'Преглед', 0),
                    (N'Companies.Create', N'Фирми', N'Създаване', 0),
                    (N'Companies.Edit', N'Фирми', N'Редакция', 0),
                    (N'Companies.Deactivate', N'Фирми', N'Активиране и деактивиране', 0),
                    (N'Partners.View', N'Контрагенти', N'Преглед', 0),
                    (N'Partners.Create', N'Контрагенти', N'Създаване', 0),
                    (N'Partners.Edit', N'Контрагенти', N'Редакция', 0),
                    (N'Partners.Deactivate', N'Контрагенти', N'Активиране и деактивиране', 0),
                    (N'Partners.Move', N'Контрагенти', N'Преместване', 0),
                    (N'PartnerGroups.View', N'Групи контрагенти', N'Преглед', 0),
                    (N'PartnerGroups.Create', N'Групи контрагенти', N'Създаване', 0),
                    (N'PartnerGroups.Edit', N'Групи контрагенти', N'Редакция', 0),
                    (N'PartnerGroups.Move', N'Групи контрагенти', N'Преместване', 0),
                    (N'Users.View', N'Потребители', N'Преглед', 0),
                    (N'Access.AssignRoles', N'Достъп', N'Даване и отнемане на роли', 1),
                    (N'Access.ManagePermissions', N'Достъп', N'Матрица на правата', 1),
                    (N'Access.ViewHistory', N'Достъп', N'Преглед на историята', 0),
                    (N'Settings.Manage', N'Настройки', N'Критични настройки', 1);

                INSERT INTO [RolePermissions] ([RoleN], [PermissionKey])
                SELECT r.[N], p.[Key] FROM [Roles] r CROSS JOIN [Permissions] p
                WHERE p.[AdminOnly] = 0 AND (
                    r.[Name] = N'Moderator'
                    OR (r.[Name] = N'CatalogEditor' AND LEFT(p.[Key], CHARINDEX('.',p.[Key])-1) IN (N'Dashboard',N'Products',N'Options',N'Categories',N'Content',N'Promotions'))
                    OR (r.[Name] = N'OrderManager' AND LEFT(p.[Key], CHARINDEX('.',p.[Key])-1) IN (N'Dashboard',N'Orders',N'Companies',N'Partners',N'PartnerGroups',N'Users')));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Automatic downgrade would erase access history and configured grants. Restore a verified backup instead.");
        }
    }
}
