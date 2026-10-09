using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.ShopMigrations
{
    /// <inheritdoc />
    public partial class AddProductDataAndTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Refuse incompatible legacy image URLs instead of truncating them.
            // The original ImageUrl column and all Bulgarian product text remain intact.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Products] WITH (TABLOCKX, HOLDLOCK)
                           WHERE [ImageUrl] IS NOT NULL AND DATALENGTH([ImageUrl]) > 4096)
                    THROW 51000, 'Product image URL exceeds the gallery limit of 2048 characters. Correct it before applying this migration.', 1;
                """);

            migrationBuilder.CreateSequence(
                name: "ProductSkuNumbers");

            migrationBuilder.AddColumn<decimal>(
                name: "DepthCm",
                table: "Products",
                type: "decimal(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HeightCm",
                table: "Products",
                type: "decimal(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Materials",
                table: "Products",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sku",
                table: "Products",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                collation: "Latin1_General_100_CI_AS");

            migrationBuilder.AddColumn<decimal>(
                name: "WidthCm",
                table: "Products",
                type: "decimal(10,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductImages",
                columns: table => new
                {
                    N = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductN = table.Column<int>(type: "int", nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImages", x => x.N);
                    table.CheckConstraint("CK_ProductImages_DisplayOrder", "[DisplayOrder] >= 0");
                    table.ForeignKey(
                        name: "FK_ProductImages_Products_ProductN",
                        column: x => x.ProductN,
                        principalTable: "Products",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductTranslations",
                columns: table => new
                {
                    ProductN = table.Column<int>(type: "int", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Materials = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductTranslations", x => new { x.ProductN, x.Language });
                    table.CheckConstraint("CK_ProductTranslations_Language", "[Language] COLLATE Latin1_General_100_BIN2 = N'en'");
                    table.ForeignKey(
                        name: "FK_ProductTranslations_Products_ProductN",
                        column: x => x.ProductN,
                        principalTable: "Products",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Cascade);
                });

            // Give existing products deterministic numeric codes without assuming
            // their primary keys are positive or contiguous. Code edits never change N.
            // Dynamic SQL defers binding of the newly added Sku column until after ALTER TABLE.
            migrationBuilder.Sql("""
                EXEC(N';WITH [NumberedProducts] AS
                (
                    SELECT [N], ROW_NUMBER() OVER (ORDER BY [N]) AS [SkuNumber]
                    FROM [Products]
                )
                UPDATE [p]
                SET [Sku] = CASE WHEN [n].[SkuNumber] < 1000000
                    THEN RIGHT(N''000000'' + CONVERT(nvarchar(20), [n].[SkuNumber]), 6)
                    ELSE CONVERT(nvarchar(20), [n].[SkuNumber]) END
                FROM [Products] AS [p]
                INNER JOIN [NumberedProducts] AS [n] ON [p].[N] = [n].[N];');

                DECLARE @NextSku bigint = (SELECT COUNT_BIG(*) + 1 FROM [Products]);
                DECLARE @RestartSequence nvarchar(200) =
                    N'ALTER SEQUENCE [ProductSkuNumbers] RESTART WITH ' + CONVERT(nvarchar(20), @NextSku);
                EXEC sys.sp_executesql @RestartSequence;

                INSERT INTO [ProductImages] ([ProductN], [ImageUrl], [DisplayOrder])
                SELECT [N], [ImageUrl], 0 FROM [Products]
                WHERE [ImageUrl] IS NOT NULL AND NULLIF(LTRIM(RTRIM([ImageUrl])), N'') IS NOT NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "Products",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                collation: "Latin1_General_100_CI_AS",
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true,
                oldCollation: "Latin1_General_100_CI_AS");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Sku",
                table: "Products",
                column: "Sku",
                unique: true);

            // Generated deployment scripts combine this migration into one SQL batch.
            // Bind checks after the new columns exist, just like the SKU backfill above.
            migrationBuilder.Sql("""
                EXEC(N'ALTER TABLE [Products] ADD CONSTRAINT [CK_Products_DepthCm]
                    CHECK ([DepthCm] IS NULL OR ([DepthCm] > 0 AND [DepthCm] <= 100000));');
                EXEC(N'ALTER TABLE [Products] ADD CONSTRAINT [CK_Products_HeightCm]
                    CHECK ([HeightCm] IS NULL OR ([HeightCm] > 0 AND [HeightCm] <= 100000));');
                EXEC(N'ALTER TABLE [Products] ADD CONSTRAINT [CK_Products_Sku]
                    CHECK (DATALENGTH([Sku]) BETWEEN 2 AND 128 AND [Sku] COLLATE Latin1_General_100_BIN2 NOT LIKE N''%[^-A-Z0-9_]%'');');
                EXEC(N'ALTER TABLE [Products] ADD CONSTRAINT [CK_Products_WidthCm]
                    CHECK ([WidthCm] IS NULL OR ([WidthCm] > 0 AND [WidthCm] <= 100000));');
                """);
            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_ProductN_DisplayOrder",
                table: "ProductImages",
                columns: new[] { "ProductN", "DisplayOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductImages");

            migrationBuilder.DropTable(
                name: "ProductTranslations");

            migrationBuilder.DropIndex(
                name: "IX_Products_Sku",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_DepthCm",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_HeightCm",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_Sku",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_WidthCm",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "DepthCm",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "HeightCm",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Materials",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Sku",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "WidthCm",
                table: "Products");

            migrationBuilder.DropSequence(
                name: "ProductSkuNumbers");
        }
    }
}
