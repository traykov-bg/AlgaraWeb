using System;
using Algara.Identity.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(IdentityDbContext))]
    [Migration("20260502000000_CreateUsersAddresses")]
    public partial class CreateUsersAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsersAddresses",
                columns: table => new
                {
                    N = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserN = table.Column<int>(type: "int", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AddressLine1 = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    AddressLine2 = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsersAddresses", x => x.N);
                    table.ForeignKey(
                        name: "FK_UsersAddresses_Users_UserN",
                        column: x => x.UserN,
                        principalTable: "Users",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsersAddresses_UserN",
                table: "UsersAddresses",
                column: "UserN");

            migrationBuilder.Sql("""
                INSERT INTO UsersAddresses
                    (UserN, FirstName, LastName, PhoneNumber, Email, AddressLine1, AddressLine2, City, PostalCode, Country, IsDefault, CreatedAt)
                SELECT
                    N,
                    COALESCE(NULLIF(FirstName, ''), N''),
                    COALESCE(NULLIF(LastName, ''), N''),
                    PhoneNumber,
                    Email,
                    AddressLine1,
                    AddressLine2,
                    COALESCE(NULLIF(City, ''), N''),
                    PostalCode,
                    COALESCE(NULLIF(Country, ''), N'България'),
                    CAST(1 AS bit),
                    GETDATE()
                FROM Users
                WHERE AddressLine1 IS NOT NULL AND LTRIM(RTRIM(AddressLine1)) <> ''
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsersAddresses");
        }
    }
}
