using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddLastLoginSessionId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastLoginSessionId",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastLoginSessionId",
                table: "Users");
        }
    }
}
