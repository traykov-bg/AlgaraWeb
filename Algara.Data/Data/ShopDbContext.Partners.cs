using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Algara.Data.Data;

public partial class ShopDbContext
{
    public DbSet<Company> Companies { get; set; }
    public DbSet<Partner> Partners { get; set; }
    public DbSet<PartnerGroup> PartnerGroups { get; set; }
    public DbSet<PartnerCatalogChange> PartnerCatalogChanges { get; set; }

    private static void ConfigurePartnerCatalog(ModelBuilder model)
    {
        const string binary = "Latin1_General_100_BIN2";
        model.HasSequence<long>("PartnerCodeNumbers").StartsAt(1);
        model.Entity<Company>(b =>
        {
            b.HasKey(c => c.N);
            b.ToTable("Companies", t =>
            {
                t.HasCheckConstraint("CK_Companies_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
                t.HasCheckConstraint("CK_Companies_Country", "DATALENGTH([CountryCode]) = 4 AND [CountryCode] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^A-Z]%'");
                t.HasCheckConstraint("CK_Companies_RegistrationType", "[RegistrationType] IN (N'EIK', N'BULSTAT', N'FOREIGN')");
            });
            b.Property(c => c.Name).HasMaxLength(200);
            b.Property(c => c.LegalForm).HasMaxLength(50);
            b.Property(c => c.CountryCode).HasMaxLength(2);
            b.Property(c => c.RegistrationType).HasMaxLength(20);
            b.Property(c => c.RegistrationNumber).HasMaxLength(64);
            b.Property(c => c.NormalizedRegistrationNumber).HasMaxLength(64).UseCollation(binary);
            b.HasIndex(c => new { c.CountryCode, c.RegistrationType, c.NormalizedRegistrationNumber }).IsUnique()
                .HasFilter("[NormalizedRegistrationNumber] IS NOT NULL");
            b.Property(c => c.VatNumber).HasMaxLength(64);
            b.Property(c => c.Representative).HasMaxLength(200);
            b.Property(c => c.Phone).HasMaxLength(16);
            b.Property(c => c.Email).HasMaxLength(256);
            b.Property(c => c.AccountingContact).HasMaxLength(500);
            b.Property(c => c.Notes).HasMaxLength(4000);
            b.Property(c => c.RowVersion).IsRowVersion();
            b.OwnsOne(c => c.Address);
            b.OwnsOne(c => c.CorrespondenceAddress);
            b.Navigation(c => c.Address).IsRequired();
            b.Navigation(c => c.CorrespondenceAddress).IsRequired();
        });
        model.Entity<PartnerGroup>(b =>
        {
            b.HasKey(g => g.N);
            b.ToTable("PartnerGroups", t =>
            {
                t.HasCheckConstraint("CK_PartnerGroups_Parent", "([N] = 0 AND [ParentN] IS NULL) OR ([N] <> 0 AND [ParentN] IS NOT NULL AND [ParentN] <> [N])");
                t.HasCheckConstraint("CK_PartnerGroups_Name", "LEN(LTRIM(RTRIM([Name]))) > 0 AND LEN([NormalizedName]) > 0");
                t.HasCheckConstraint("CK_PartnerGroups_Order", "[DisplayOrder] >= 0");
            });
            b.Property(g => g.Name).HasMaxLength(150);
            b.Property(g => g.NormalizedName).HasMaxLength(150).UseCollation(binary);
            b.Property(g => g.RowVersion).IsRowVersion();
            b.HasOne(g => g.Parent).WithMany().HasForeignKey(g => g.ParentN).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(g => new { g.ParentN, g.NormalizedName }).IsUnique().HasFilter(null);
        });
        model.Entity<Partner>(b =>
        {
            b.HasKey(p => p.N);
            b.ToTable("Partners", t =>
            {
                t.HasCheckConstraint("CK_Partners_Code", "LEN([Code]) > 0 AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%'");
                t.HasCheckConstraint("CK_Partners_Kind", "([CompanyN] IS NULL AND [Kind] = 0) OR ([CompanyN] IS NOT NULL AND [Kind] = 1)");
                t.HasCheckConstraint("CK_Partners_Identity", "LEN(LTRIM(RTRIM([SiteName]))) > 0 AND LEN([NormalizedSiteName]) > 0");
                t.HasCheckConstraint("CK_Partners_Phone", "[Phone] IS NULL OR [Phone] = N'0' OR (LEFT([Phone],1) = N'+' AND LEN([Phone]) BETWEEN 8 AND 16 AND SUBSTRING([Phone],2,15) COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%')");
            });
            b.Property(p => p.Code).HasMaxLength(32).UseCollation(binary);
            b.HasIndex(p => p.Code).IsUnique();
            b.Property(p => p.Kind).HasConversion<int>();
            foreach (var name in new[] { "FirstName", "MiddleName", "LastName", "NormalizedFirstName", "NormalizedLastName" })
                b.Property<string>(name).HasMaxLength(100).UseCollation(binary);
            b.Property(p => p.SiteName).HasMaxLength(400).IsRequired();
            b.Property(p => p.NormalizedSiteName).HasMaxLength(400).UseCollation(binary).IsRequired();
            b.Property(p => p.Phone).HasMaxLength(16);
            b.HasIndex(p => p.NormalizedSiteName).IsUnique().HasFilter(null);
            b.Property(p => p.Email).HasMaxLength(256);
            b.Property(p => p.ContactPerson).HasMaxLength(200);
            b.Property(p => p.Notes).HasMaxLength(4000);
            b.Property(p => p.RowVersion).IsRowVersion();
            b.HasOne(p => p.Company).WithMany().HasForeignKey(p => p.CompanyN).OnDelete(DeleteBehavior.Restrict);
            b.Property(p => p.GroupN).HasDefaultValue(0);
            b.HasOne(p => p.Group).WithMany().HasForeignKey(p => p.GroupN).OnDelete(DeleteBehavior.Restrict);
            b.OwnsOne(p => p.Address);
            b.OwnsOne(p => p.DeliveryAddress);
            b.Navigation(p => p.Address).IsRequired();
            b.Navigation(p => p.DeliveryAddress).IsRequired();
        });
        model.Entity<PartnerCatalogChange>(b =>
        {
            b.HasKey(c => c.N);
            b.Property(c => c.EntityType).HasMaxLength(20);
            b.Property(c => c.ActorId).HasMaxLength(128);
            b.Property(c => c.ActorName).HasMaxLength(256);
            b.HasIndex(c => new { c.EntityType, c.EntityN, c.CreatedAtUtc });
        });
    }
}
