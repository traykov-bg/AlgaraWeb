using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerDossierPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Frozen definitions only. Existing role assignments and grants remain unchanged;
            // an administrator explicitly grants access to customer dossiers after deployment.
            migrationBuilder.Sql("""
                INSERT INTO [Permissions] ([Key], [Module], [Name], [AdminOnly]) VALUES
                    (N'Users.ViewDossier', N'Потребители', N'Преглед на клиентско досие', 0),
                    (N'Users.AddNotes', N'Потребители', N'Добавяне на вътрешни бележки', 0),
                    (N'Users.ManagePartnerLinks', N'Потребители', N'Свързване и откачане на контрагент', 0),
                    (N'Users.ViewDossierHistory', N'Потребители', N'Преглед на историята на досието', 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Automatic downgrade would remove configured customer-dossier grants. Restore a verified backup instead.");
        }
    }
}
