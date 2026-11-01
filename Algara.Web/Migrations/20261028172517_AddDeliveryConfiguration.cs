using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Algara.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CourierConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ProtectedPassword = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: true),
                    SenderClientNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SenderOfficeCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SenderName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SenderPhone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ServiceId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourierConfigurations", x => x.Id);
                    table.CheckConstraint("CK_CourierConfigurations_EnabledSecret", "[Enabled] = 0 OR ([ProtectedPassword] IS NOT NULL AND LEN([ProtectedPassword]) > 0)");
                    table.CheckConstraint("CK_CourierConfigurations_Provider", "[Id] IN (1,2)");
                });

            migrationBuilder.CreateTable(
                name: "TransportConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    OriginLatitude = table.Column<decimal>(type: "decimal(10,7)", precision: 10, scale: 7, nullable: true),
                    OriginLongitude = table.Column<decimal>(type: "decimal(10,7)", precision: 10, scale: 7, nullable: true),
                    GeocodingEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ProtectedGeocodingKey = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportConfigurations", x => x.Id);
                    table.CheckConstraint("CK_TransportConfigurations_EnabledSecret", "[GeocodingEnabled] = 0 OR ([OriginLatitude] IS NOT NULL AND [OriginLongitude] IS NOT NULL AND [ProtectedGeocodingKey] IS NOT NULL AND LEN([ProtectedGeocodingKey]) > 0)");
                    table.CheckConstraint("CK_TransportConfigurations_Origin", "([OriginLatitude] IS NULL AND [OriginLongitude] IS NULL) OR ([OriginLatitude] IS NOT NULL AND [OriginLongitude] IS NOT NULL AND [OriginLatitude] BETWEEN -90 AND 90 AND [OriginLongitude] BETWEEN -180 AND 180)");
                    table.CheckConstraint("CK_TransportConfigurations_Singleton", "[Id] = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Automatic downgrade would discard protected delivery configuration. Restore a verified backup instead.");
        }
    }
}
