using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.ShopMigrations
{
    /// <inheritdoc />
    public partial class SimplifyOptionCatalogCodesAndFabricImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Fabrics_Code",
                table: "Fabrics");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CatalogColors_Code",
                table: "CatalogColors");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Fabrics",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                collation: "Latin1_General_100_CI_AS",
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldCollation: "Latin1_General_100_CI_AS");

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Fabrics",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Fabrics_Code",
                table: "Fabrics",
                sql: "DATALENGTH([Code]) BETWEEN 2 AND 400 AND LEN(LTRIM(RTRIM([Code]))) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CatalogColors_Code",
                table: "CatalogColors",
                sql: "DATALENGTH([Code]) BETWEEN 2 AND 128 AND LEN(LTRIM(RTRIM([Code]))) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Never truncate or silently rewrite codes when returning to the old ASCII-only schema.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Fabrics]
                    WHERE DATALENGTH([Code]) NOT BETWEEN 2 AND 128
                       OR [Code] COLLATE Latin1_General_100_BIN2 LIKE N'%[^-A-Z0-9_]%')
                   OR EXISTS (SELECT 1 FROM [CatalogColors]
                    WHERE DATALENGTH([Code]) NOT BETWEEN 2 AND 128
                       OR [Code] COLLATE Latin1_General_100_BIN2 LIKE N'%[^-A-Z0-9_]%')
                    THROW 51000, 'Cannot downgrade: saved catalog codes are incompatible with the former 64-character ASCII-only format. Review the codes before retrying.', 1;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_Fabrics_Code",
                table: "Fabrics");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CatalogColors_Code",
                table: "CatalogColors");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Fabrics");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Fabrics",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                collation: "Latin1_General_100_CI_AS",
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldCollation: "Latin1_General_100_CI_AS");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Fabrics_Code",
                table: "Fabrics",
                sql: "DATALENGTH([Code]) BETWEEN 2 AND 128 AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^-A-Z0-9_]%'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CatalogColors_Code",
                table: "CatalogColors",
                sql: "DATALENGTH([Code]) BETWEEN 2 AND 128 AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^-A-Z0-9_]%'");
        }
    }
}
