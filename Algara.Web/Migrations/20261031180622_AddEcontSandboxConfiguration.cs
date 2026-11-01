using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddEcontSandboxConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UseSandbox",
                table: "CourierConfigurations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Resolve the new column after ALTER TABLE has executed, including
            // when an idempotent deployment script wraps this in one batch.
            migrationBuilder.Sql("EXEC(N'ALTER TABLE [CourierConfigurations] ADD CONSTRAINT [CK_CourierConfigurations_Sandbox] CHECK ([UseSandbox] = 0 OR [Id] = 1);');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Automatic downgrade would discard courier environment selection and could route test credentials to production. Restore a verified backup instead.");
        }
    }
}
