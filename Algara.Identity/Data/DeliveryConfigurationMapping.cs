using Algara.Identity.Models;
using Microsoft.EntityFrameworkCore;

namespace Algara.Identity.Data;

public static class DeliveryConfigurationMapping
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<CourierConfiguration>(b =>
        {
            b.ToTable("CourierConfigurations", t =>
            {
                t.HasCheckConstraint("CK_CourierConfigurations_Provider", "[Id] IN (1,2)");
                t.HasCheckConstraint("CK_CourierConfigurations_Sandbox", "[UseSandbox] = 0 OR [Id] = 1");
                t.HasCheckConstraint("CK_CourierConfigurations_EnabledSecret", "[Enabled] = 0 OR ([ProtectedPassword] IS NOT NULL AND LEN([ProtectedPassword]) > 0)");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.UseSandbox).HasDefaultValue(false);
            b.Property(x => x.UserName).HasMaxLength(256);
            b.Property(x => x.ProtectedPassword).HasMaxLength(8192);
            b.Property(x => x.SenderClientNumber).HasMaxLength(64);
            b.Property(x => x.SenderOfficeCode).HasMaxLength(64);
            b.Property(x => x.SenderName).HasMaxLength(200);
            b.Property(x => x.SenderPhone).HasMaxLength(40);
            b.Property(x => x.RowVersion).IsRowVersion();
        });
        builder.Entity<TransportConfiguration>(b =>
        {
            b.ToTable("TransportConfigurations", t =>
            {
                t.HasCheckConstraint("CK_TransportConfigurations_Singleton", "[Id] = 1");
                t.HasCheckConstraint("CK_TransportConfigurations_Origin", "([OriginLatitude] IS NULL AND [OriginLongitude] IS NULL) OR ([OriginLatitude] IS NOT NULL AND [OriginLongitude] IS NOT NULL AND [OriginLatitude] BETWEEN -90 AND 90 AND [OriginLongitude] BETWEEN -180 AND 180)");
                t.HasCheckConstraint("CK_TransportConfigurations_EnabledSecret", "[GeocodingEnabled] = 0 OR ([OriginLatitude] IS NOT NULL AND [OriginLongitude] IS NOT NULL AND [ProtectedGeocodingKey] IS NOT NULL AND LEN([ProtectedGeocodingKey]) > 0)");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.OriginLatitude).HasPrecision(10, 7);
            b.Property(x => x.OriginLongitude).HasPrecision(10, 7);
            b.Property(x => x.ProtectedGeocodingKey).HasMaxLength(8192);
            b.Property(x => x.RowVersion).IsRowVersion();
        });
    }
}
