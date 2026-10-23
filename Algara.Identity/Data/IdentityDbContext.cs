using Algara.Identity.Models;
using Microsoft.EntityFrameworkCore;

namespace Algara.Identity.Data
{
    public class IdentityDbContext : DbContext
    {
        public IdentityDbContext(DbContextOptions<IdentityDbContext> options)
            : base(options) { }

        public DbSet<ApplicationUser> Users { get; set; }
        public DbSet<ApplicationRole> Roles { get; set; }
        public DbSet<UserRole> UserRoles { get; set; }
        public DbSet<UserSession> UserSessions { get; set; }
        public DbSet<UserConsent> UserConsents { get; set; }
        public DbSet<UserAddress> UsersAddresses { get; set; }
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<RolePermission> RolePermissions { get; set; }
        public DbSet<AccessAuditEvent> AccessAuditEvents { get; set; }
        public DbSet<SmtpConfiguration> SmtpConfigurations { get; set; }
        public DbSet<SmtpSettingsAuditEvent> SmtpSettingsAuditEvents { get; set; }
        public DbSet<AccountEmailToken> AccountEmailTokens { get; set; }
        public DbSet<AccountEmailAuditEvent> AccountEmailAuditEvents { get; set; }
        public DbSet<EmailOutboxMessage> EmailOutboxMessages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            AccountEmailMapping.Configure(modelBuilder);
            EmailOutboxMapping.Configure(modelBuilder);
            modelBuilder.Entity<ApplicationUser>()
                .HasKey(u => u.N); // Казваме, че Primary Key е N

            // Non-clustered index за Id
            modelBuilder.Entity<ApplicationUser>()
                .HasIndex(u => u.Id, "IX_Users_Id") // Дава име на индекса
                .IsUnique();

            modelBuilder.Entity<ApplicationUser>()
                .Property(u => u.Email)
                .HasMaxLength(256);

            modelBuilder.Entity<ApplicationUser>()
                .HasIndex(u => u.Email, "IX_Users_Email")
                .IsUnique();

            modelBuilder.Entity<ApplicationRole>()
                .HasKey(r => r.N); // Казваме, че Primary Key е N

            modelBuilder.Entity<ApplicationRole>().Property(r => r.PermissionsVersion).IsConcurrencyToken();
            modelBuilder.Entity<ApplicationRole>().Property(r => r.Name).HasMaxLength(256).IsRequired();
            modelBuilder.Entity<ApplicationRole>().HasIndex(r => r.Name).IsUnique();
            modelBuilder.Entity<Permission>(b =>
            {
                b.HasKey(p => p.Key);
                b.Property(p => p.Key).HasMaxLength(100);
                b.Property(p => p.Module).HasMaxLength(100).IsRequired();
                b.Property(p => p.Name).HasMaxLength(200).IsRequired();
            });
            modelBuilder.Entity<RolePermission>(b =>
            {
                b.HasKey(p => new { p.RoleN, p.PermissionKey });
                b.Property(p => p.PermissionKey).HasMaxLength(100);
                b.HasOne(p => p.Role).WithMany().HasForeignKey(p => p.RoleN).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(p => p.Permission).WithMany().HasForeignKey(p => p.PermissionKey).OnDelete(DeleteBehavior.Restrict);
            });
            modelBuilder.Entity<AccessAuditEvent>(b =>
            {
                b.HasKey(e => e.Id);
                b.Property(e => e.ActorId).HasMaxLength(100).IsRequired();
                b.Property(e => e.ActorName).HasMaxLength(256).IsRequired();
                b.Property(e => e.Action).HasMaxLength(100).IsRequired();
                b.Property(e => e.Target).HasMaxLength(256).IsRequired();
                b.Property(e => e.TargetName).HasMaxLength(256).IsRequired();
                b.Property(e => e.BeforeJson).IsRequired();
                b.Property(e => e.AfterJson).IsRequired();
                b.HasIndex(e => e.OccurredAtUtc);
            });
            modelBuilder.Entity<SmtpConfiguration>(b =>
            {
                b.ToTable("SmtpConfigurations", table =>
                {
                    table.HasCheckConstraint("CK_SmtpConfigurations_Singleton", "[Id] = 1");
                    table.HasCheckConstraint("CK_SmtpConfigurations_Port", "[Port] BETWEEN 1 AND 65535");
                    table.HasCheckConstraint("CK_SmtpConfigurations_SecurityMode", "[SecurityMode] IN (1, 2)");
                    table.HasCheckConstraint("CK_SmtpConfigurations_SecurePort", "([Port] <> 465 OR [SecurityMode] = 2) AND ([Port] <> 587 OR [SecurityMode] = 1)");
                    table.HasCheckConstraint("CK_SmtpConfigurations_EnabledPassword", "[Enabled] = 0 OR ([ProtectedPassword] IS NOT NULL AND LEN([ProtectedPassword]) > 0)");
                });
                b.HasKey(c => c.Id);
                b.Property(c => c.Id).ValueGeneratedNever();
                b.Property(c => c.Host).HasMaxLength(253).IsRequired();
                b.Property(c => c.Username).HasMaxLength(256).IsRequired();
                b.Property(c => c.FromEmail).HasMaxLength(256).IsRequired();
                b.Property(c => c.FromName).HasMaxLength(200).IsRequired();
                b.Property(c => c.PublicSiteUrl).HasMaxLength(2048).IsRequired();
                b.Property(c => c.ProtectedPassword).HasMaxLength(8192);
                b.Property(c => c.RowVersion).IsRowVersion();
            });
            modelBuilder.Entity<SmtpSettingsAuditEvent>(b =>
            {
                b.HasKey(e => e.Id);
                b.Property(e => e.Id).UseIdentityColumn();
                b.Property(e => e.ActorId).HasMaxLength(128).IsRequired();
                b.Property(e => e.ActorName).HasMaxLength(256).IsRequired();
                b.Property(e => e.Action).HasMaxLength(50).IsRequired();
                b.Property(e => e.BeforeJson).IsRequired();
                b.Property(e => e.AfterJson).IsRequired();
                b.HasIndex(e => e.OccurredAtUtc);
            });

            // Non-clustered index за Role Id
            modelBuilder.Entity<ApplicationRole>()
                .HasIndex(r => r.Id, "IX_Roles_Id") // Дава име на индекса
                .IsUnique();

            modelBuilder.Entity<UserRole>()
                .HasKey(ur => new { ur.UserN, ur.RoleN });

            modelBuilder.Entity<UserRole>()
                .HasOne(ur => ur.User)
                .WithMany()
                .HasForeignKey(ur => ur.UserN)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserRole>()
                .HasOne(ur => ur.Role)
                .WithMany()
                .HasForeignKey(ur => ur.RoleN)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserSession>()
                .HasKey(us => us.Id);

            modelBuilder.Entity<UserSession>()
                .HasOne(us => us.User)
                .WithMany(u => u.UserSessions)
                .HasForeignKey(us => us.UserN)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserConsent>()
                .HasKey(uc => uc.Id);

            modelBuilder.Entity<UserConsent>()
                .HasOne(uc => uc.User)
                .WithMany()
                .HasForeignKey(uc => uc.UserN)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserConsent>()
                .HasIndex(uc => new { uc.UserN, uc.ConsentType });

            modelBuilder.Entity<UserConsent>()
                .Property(uc => uc.ConsentType)
                .HasMaxLength(32)
                .IsRequired();

            modelBuilder.Entity<UserAddress>(b =>
            {
                b.ToTable("UsersAddresses");
                b.HasKey(ua => ua.N);

                b.HasOne(ua => ua.User)
                    .WithMany(u => u.Addresses)
                    .HasForeignKey(ua => ua.UserN)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasIndex(ua => ua.UserN);
                b.Property(ua => ua.FirstName).HasMaxLength(100).IsRequired();
                b.Property(ua => ua.LastName).HasMaxLength(100).IsRequired();
                b.Property(ua => ua.PhoneNumber).HasMaxLength(40);
                b.Property(ua => ua.Email).HasMaxLength(256).IsRequired();
                b.Property(ua => ua.AddressLine1).HasMaxLength(250).IsRequired();
                b.Property(ua => ua.AddressLine2).HasMaxLength(250);
                b.Property(ua => ua.City).HasMaxLength(100).IsRequired();
                b.Property(ua => ua.PostalCode).HasMaxLength(20);
                b.Property(ua => ua.Country).HasMaxLength(100).IsRequired();
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}
