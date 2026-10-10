using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.ShopMigrations
{
    /// <inheritdoc />
    public partial class AddProductVariantsAndAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "ProductVariantSkuNumbers");

            migrationBuilder.AddColumn<bool>(
                name: "IsUpholstered",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OptionKind",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Products",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "StockQuantity",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ProductImages_ProductN_N",
                table: "ProductImages",
                columns: new[] { "ProductN", "N" });

            migrationBuilder.CreateTable(
                name: "Fabrics",
                columns: table => new
                {
                    N = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EnglishName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    EnglishDescription = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fabrics", x => x.N);
                    table.CheckConstraint("CK_Fabrics_Code", "DATALENGTH([Code]) BETWEEN 2 AND 128 AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^-A-Z0-9_]%'");
                });

            migrationBuilder.CreateTable(
                name: "CatalogColors",
                columns: table => new
                {
                    N = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FabricN = table.Column<int>(type: "int", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EnglishName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    HexColor = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: true),
                    ColorGroup = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ImageUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogColors", x => x.N);
                    table.CheckConstraint("CK_CatalogColors_Code", "DATALENGTH([Code]) BETWEEN 2 AND 128 AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^-A-Z0-9_]%'");
                    table.CheckConstraint("CK_CatalogColors_HexColor", "[HexColor] IS NULL OR (DATALENGTH([HexColor]) = 14 AND LEFT([HexColor], 1) = N'#' AND SUBSTRING([HexColor], 2, 6) COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9A-Fa-f]%')");
                    table.ForeignKey(
                        name: "FK_CatalogColors_Fabrics_FabricN",
                        column: x => x.FabricN,
                        principalTable: "Fabrics",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductFabricSurcharges",
                columns: table => new
                {
                    ProductN = table.Column<int>(type: "int", nullable: false),
                    FabricN = table.Column<int>(type: "int", nullable: false),
                    Surcharge = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductFabricSurcharges", x => new { x.ProductN, x.FabricN });
                    table.CheckConstraint("CK_ProductFabricSurcharges_Surcharge", "[Surcharge] >= 0");
                    table.ForeignKey(
                        name: "FK_ProductFabricSurcharges_Fabrics_FabricN",
                        column: x => x.FabricN,
                        principalTable: "Fabrics",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductFabricSurcharges_Products_ProductN",
                        column: x => x.ProductN,
                        principalTable: "Products",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariants",
                columns: table => new
                {
                    N = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductN = table.Column<int>(type: "int", nullable: false),
                    CatalogColorN = table.Column<int>(type: "int", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    StockQuantity = table.Column<int>(type: "int", nullable: true),
                    Surcharge = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ProductImageN = table.Column<int>(type: "int", nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariants", x => x.N);
                    table.CheckConstraint("CK_ProductVariants_Sku", "DATALENGTH([Sku]) BETWEEN 2 AND 128 AND [Sku] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^-A-Z0-9_]%'");
                    table.CheckConstraint("CK_ProductVariants_StockQuantity", "[StockQuantity] IS NULL OR ([StockQuantity] >= 0 AND [StockQuantity] <= 1000000)");
                    table.CheckConstraint("CK_ProductVariants_Surcharge", "[Surcharge] >= 0");
                    table.ForeignKey(
                        name: "FK_ProductVariants_CatalogColors_CatalogColorN",
                        column: x => x.CatalogColorN,
                        principalTable: "CatalogColors",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductVariants_ProductImages_ProductN_ProductImageN",
                        columns: x => new { x.ProductN, x.ProductImageN },
                        principalTable: "ProductImages",
                        principalColumns: new[] { "ProductN", "N" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductVariants_Products_ProductN",
                        column: x => x.ProductN,
                        principalTable: "Products",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("EXEC(N'ALTER TABLE [Products] ADD CONSTRAINT [CK_Products_OptionKind] CHECK ([OptionKind] IN (0, 1, 2));');");

            migrationBuilder.Sql("EXEC(N'ALTER TABLE [Products] ADD CONSTRAINT [CK_Products_StockQuantity] CHECK ([StockQuantity] IS NULL OR ([StockQuantity] >= 0 AND [StockQuantity] <= 1000000));');");

            migrationBuilder.Sql("EXEC(N'ALTER TABLE [Products] ADD CONSTRAINT [CK_Products_VariantStock] CHECK ([OptionKind] = 0 OR [StockQuantity] IS NULL);');");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogColors_FabricN_Code",
                table: "CatalogColors",
                columns: new[] { "FabricN", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fabrics_Code",
                table: "Fabrics",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductFabricSurcharges_FabricN",
                table: "ProductFabricSurcharges",
                column: "FabricN");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_CatalogColorN",
                table: "ProductVariants",
                column: "CatalogColorN");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductN_CatalogColorN",
                table: "ProductVariants",
                columns: new[] { "ProductN", "CatalogColorN" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductN_ProductImageN",
                table: "ProductVariants",
                columns: new[] { "ProductN", "ProductImageN" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_Sku",
                table: "ProductVariants",
                column: "Sku",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductFabricSurcharges");

            migrationBuilder.DropTable(
                name: "ProductVariants");

            migrationBuilder.DropTable(
                name: "CatalogColors");

            migrationBuilder.DropTable(
                name: "Fabrics");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_OptionKind",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_StockQuantity",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_VariantStock",
                table: "Products");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ProductImages_ProductN_N",
                table: "ProductImages");

            migrationBuilder.DropColumn(
                name: "IsUpholstered",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "OptionKind",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "StockQuantity",
                table: "Products");

            migrationBuilder.DropSequence(
                name: "ProductVariantSkuNumbers");
        }
    }
}
