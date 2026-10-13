using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.ShopMigrations
{
    /// <inheritdoc />
    public partial class AddCompaniesAndPartners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "PartnerCodeNumbers");

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    N = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LegalForm = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CountryCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    RegistrationType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RegistrationNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    NormalizedRegistrationNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    IsVatRegistered = table.Column<bool>(type: "bit", nullable: false),
                    VatNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Representative = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AccountingContact = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Address_Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Address_PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Address_City = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Address_Street = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Address_Building = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Address_Entrance = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Address_Floor = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Address_Apartment = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Address_Details = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CorrespondenceAddress_Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CorrespondenceAddress_PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CorrespondenceAddress_City = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CorrespondenceAddress_Street = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CorrespondenceAddress_Building = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CorrespondenceAddress_Entrance = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CorrespondenceAddress_Floor = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CorrespondenceAddress_Apartment = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CorrespondenceAddress_Details = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.N);
                    table.CheckConstraint("CK_Companies_Country", "DATALENGTH([CountryCode]) = 4 AND [CountryCode] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^A-Z]%'");
                    table.CheckConstraint("CK_Companies_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
                    table.CheckConstraint("CK_Companies_RegistrationType", "[RegistrationType] IN (N'EIK', N'BULSTAT', N'FOREIGN')");
                });

            migrationBuilder.CreateTable(
                name: "PartnerCatalogChanges",
                columns: table => new
                {
                    N = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EntityN = table.Column<int>(type: "int", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartnerCatalogChanges", x => x.N);
                });

            migrationBuilder.CreateTable(
                name: "PartnerGroups",
                columns: table => new
                {
                    N = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ParentN = table.Column<int>(type: "int", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartnerGroups", x => x.N);
                    table.CheckConstraint("CK_PartnerGroups_Name", "LEN(LTRIM(RTRIM([Name]))) > 0 AND LEN([NormalizedName]) > 0");
                    table.CheckConstraint("CK_PartnerGroups_Order", "[DisplayOrder] >= 0");
                    table.CheckConstraint("CK_PartnerGroups_Parent", "[ParentN] IS NULL OR [ParentN] <> [N]");
                    table.ForeignKey(
                        name: "FK_PartnerGroups_PartnerGroups_ParentN",
                        column: x => x.ParentN,
                        principalTable: "PartnerGroups",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Partners",
                columns: table => new
                {
                    N = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true, collation: "Latin1_General_100_BIN2"),
                    MiddleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true, collation: "Latin1_General_100_BIN2"),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true, collation: "Latin1_General_100_BIN2"),
                    SiteName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NormalizedFirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true, collation: "Latin1_General_100_BIN2"),
                    NormalizedLastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true, collation: "Latin1_General_100_BIN2"),
                    NormalizedSiteName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CompanyN = table.Column<int>(type: "int", nullable: true),
                    GroupN = table.Column<int>(type: "int", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ContactPerson = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Address_Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Address_PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Address_City = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Address_Street = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Address_Building = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Address_Entrance = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Address_Floor = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Address_Apartment = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Address_Details = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DeliveryAddress_Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeliveryAddress_PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DeliveryAddress_City = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DeliveryAddress_Street = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DeliveryAddress_Building = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    DeliveryAddress_Entrance = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    DeliveryAddress_Floor = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    DeliveryAddress_Apartment = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    DeliveryAddress_Details = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Partners", x => x.N);
                    table.CheckConstraint("CK_Partners_Code", "LEN([Code]) > 0 AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%'");
                    table.CheckConstraint("CK_Partners_Identity", "([Kind] = 0 AND [CompanyN] IS NULL AND [FirstName] IS NOT NULL AND LEN([FirstName]) > 0 AND [LastName] IS NOT NULL AND LEN([LastName]) > 0 AND [NormalizedFirstName] IS NOT NULL AND LEN([NormalizedFirstName]) > 0 AND [NormalizedLastName] IS NOT NULL AND LEN([NormalizedLastName]) > 0 AND [Phone] IS NOT NULL AND LEN([Phone]) >= 8 AND [SiteName] IS NULL) OR ([Kind] IN (1,2) AND [SiteName] IS NOT NULL AND LEN([SiteName]) > 0 AND [NormalizedSiteName] IS NOT NULL AND LEN([NormalizedSiteName]) > 0 AND [FirstName] IS NULL AND [MiddleName] IS NULL AND [LastName] IS NULL AND (([Kind] = 1 AND [CompanyN] IS NOT NULL) OR ([Kind] = 2 AND [CompanyN] IS NULL)))");
                    table.CheckConstraint("CK_Partners_Kind", "[Kind] IN (0,1,2)");
                    table.CheckConstraint("CK_Partners_Phone", "[Phone] IS NULL OR (LEFT([Phone],1) = N'+' AND LEN([Phone]) BETWEEN 8 AND 16 AND SUBSTRING([Phone],2,15) COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%')");
                    table.ForeignKey(
                        name: "FK_Partners_Companies_CompanyN",
                        column: x => x.CompanyN,
                        principalTable: "Companies",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Partners_PartnerGroups_GroupN",
                        column: x => x.GroupN,
                        principalTable: "PartnerGroups",
                        principalColumn: "N",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Companies_CountryCode_RegistrationType_NormalizedRegistrationNumber",
                table: "Companies",
                columns: new[] { "CountryCode", "RegistrationType", "NormalizedRegistrationNumber" },
                unique: true,
                filter: "[NormalizedRegistrationNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerCatalogChanges_EntityType_EntityN_CreatedAtUtc",
                table: "PartnerCatalogChanges",
                columns: new[] { "EntityType", "EntityN", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PartnerGroups_ParentN_NormalizedName",
                table: "PartnerGroups",
                columns: new[] { "ParentN", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partners_Code",
                table: "Partners",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partners_CompanySite",
                table: "Partners",
                columns: new[] { "CompanyN", "NormalizedSiteName" },
                unique: true,
                filter: "[Kind] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Partners_GroupN",
                table: "Partners",
                column: "GroupN");

            migrationBuilder.CreateIndex(
                name: "IX_Partners_PersonIdentity",
                table: "Partners",
                columns: new[] { "NormalizedFirstName", "NormalizedLastName", "Phone" },
                unique: true,
                filter: "[Kind] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PartnerCatalogChanges");

            migrationBuilder.DropTable(
                name: "Partners");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropTable(
                name: "PartnerGroups");

            migrationBuilder.DropSequence(
                name: "PartnerCodeNumbers");
        }
    }
}
