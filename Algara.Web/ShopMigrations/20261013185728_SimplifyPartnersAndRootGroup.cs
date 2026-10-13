using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.ShopMigrations
{
    /// <inheritdoc />
    public partial class SimplifyPartnersAndRootGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Project legacy names before changing the schema. Existing application writes have
            // already normalized Unicode; normalize whitespace and case for the new shared key.
            migrationBuilder.Sql("""
                SELECT [N], [Code], CAST(CASE WHEN [Kind] = 0
                    THEN CONCAT_WS(N' ', NULLIF([FirstName], N''), NULLIF([MiddleName], N''), NULLIF([LastName], N''))
                    ELSE [SiteName] END AS nvarchar(400)) COLLATE Cyrillic_General_100_CS_AS AS [Name]
                INTO #PartnerNamesForRevision
                FROM [Partners] WITH (TABLOCKX, HOLDLOCK);

                DECLARE @spaces nvarchar(32) = NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(133)+NCHAR(160)
                    +NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)
                    +NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288);
                DECLARE @i int = 1;
                WHILE @i <= LEN(@spaces)
                BEGIN
                    UPDATE #PartnerNamesForRevision SET [Name] = REPLACE([Name], SUBSTRING(@spaces, @i, 1), N' ');
                    SET @i += 1;
                END;
                WHILE EXISTS (SELECT 1 FROM #PartnerNamesForRevision WHERE CHARINDEX(N'  ', [Name]) > 0)
                    UPDATE #PartnerNamesForRevision SET [Name] = REPLACE([Name], N'  ', N' ');
                UPDATE #PartnerNamesForRevision SET [Name] = LTRIM(RTRIM([Name]));

                IF EXISTS (SELECT 1 FROM #PartnerNamesForRevision WHERE [Name] IS NULL OR LEN([Name]) = 0)
                    THROW 51003, N'Контрагент без име. Попълнете имената преди миграцията.', 1;

                SELECT [N], [Code], [Name],
                    UPPER([Name] COLLATE Cyrillic_General_100_CS_AS) COLLATE Latin1_General_100_BIN2 AS [NameKey]
                INTO #PartnerKeysForRevision FROM #PartnerNamesForRevision;

                IF EXISTS (SELECT [NameKey] FROM #PartnerKeysForRevision GROUP BY [NameKey] HAVING COUNT(*) > 1)
                BEGIN
                    DECLARE @conflicts nvarchar(max);
                    SELECT @conflicts = STRING_AGG(CAST(CONCAT(N'N=', [N], N', код=', [Code] COLLATE Cyrillic_General_100_CS_AS, N': ', LEFT([Name], 40)) AS nvarchar(max)), N'; ')
                    FROM (SELECT TOP (10) [N], [Code], [Name] FROM #PartnerKeysForRevision
                        WHERE [NameKey] IN (SELECT [NameKey] FROM #PartnerKeysForRevision GROUP BY [NameKey] HAVING COUNT(*) > 1)
                        ORDER BY [N]) AS duplicates;
                    DECLARE @message nvarchar(2048) = N'Дублирани имена на контрагенти. Разграничете имената в текущата картотека и повторете миграцията. ' + LEFT(@conflicts, 1800);
                    THROW 51003, @message, 1;
                END;
                IF EXISTS (SELECT 1 FROM [PartnerGroups] WITH (TABLOCKX, HOLDLOCK) WHERE [N] = 0)
                    THROW 51003, N'Вече съществува група с N=0. Необходимо е преглеждане преди създаване на основната група.', 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Partners_CompanySite",
                table: "Partners");

            migrationBuilder.DropIndex(
                name: "IX_Partners_PersonIdentity",
                table: "Partners");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Partners_Identity",
                table: "Partners");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Partners_Kind",
                table: "Partners");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Partners_Phone",
                table: "Partners");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PartnerGroups_Parent",
                table: "PartnerGroups");

            // Temporarily remove sibling uniqueness so an existing top-level group named
            // "Основна група" remains intact when the real root receives that name.
            migrationBuilder.DropIndex(name: "IX_PartnerGroups_ParentN_NormalizedName", table: "PartnerGroups");
            migrationBuilder.Sql("""
                SET IDENTITY_INSERT [PartnerGroups] ON;
                INSERT INTO [PartnerGroups] ([N], [Name], [NormalizedName], [ParentN], [DisplayOrder])
                    VALUES (0, N'Основна група', N'ОСНОВНА ГРУПА', NULL, 0);
                SET IDENTITY_INSERT [PartnerGroups] OFF;
                UPDATE [PartnerGroups] SET [ParentN] = 0 WHERE [N] <> 0 AND [ParentN] IS NULL;
                """);
            migrationBuilder.CreateIndex(name: "IX_PartnerGroups_ParentN_NormalizedName",
                table: "PartnerGroups", columns: new[] { "ParentN", "NormalizedName" }, unique: true);

            migrationBuilder.AlterColumn<string>(
                name: "SiteName",
                table: "Partners",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedSiteName",
                table: "Partners",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "",
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<int>(
                name: "GroupN",
                table: "Partners",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.Sql("""
                UPDATE p SET [SiteName] = names.[Name], [NormalizedSiteName] = names.[NameKey],
                    [Kind] = CASE WHEN p.[CompanyN] IS NULL THEN 0 ELSE 1 END
                FROM [Partners] p JOIN #PartnerKeysForRevision names ON names.[N] = p.[N];
                DROP TABLE #PartnerKeysForRevision;
                DROP TABLE #PartnerNamesForRevision;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Partners_CompanyN",
                table: "Partners",
                column: "CompanyN");

            migrationBuilder.CreateIndex(
                name: "IX_Partners_NormalizedSiteName",
                table: "Partners",
                column: "NormalizedSiteName",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Partners_Identity",
                table: "Partners",
                sql: "LEN(LTRIM(RTRIM([SiteName]))) > 0 AND LEN([NormalizedSiteName]) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Partners_Kind",
                table: "Partners",
                sql: "([CompanyN] IS NULL AND [Kind] = 0) OR ([CompanyN] IS NOT NULL AND [Kind] = 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Partners_Phone",
                table: "Partners",
                sql: "[Phone] IS NULL OR [Phone] = N'0' OR (LEFT([Phone],1) = N'+' AND LEN([Phone]) BETWEEN 8 AND 16 AND SUBSTRING([Phone],2,15) COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PartnerGroups_Parent",
                table: "PartnerGroups",
                sql: "([N] = 0 AND [ParentN] IS NULL) OR ([N] <> 0 AND [ParentN] IS NOT NULL AND [ParentN] <> [N])");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // New records no longer have recoverable three-part identities or required phones.
            // Reject a lossy automatic downgrade; restore a pre-migration backup if needed.
            throw new NotSupportedException("Връщането на стария модел изисква възстановяване от резервно копие преди тази миграция.");
        }
    }
}