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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ApplicationUser>()
                .HasKey(u => u.N); // Казваме, че Primary Key е N

            // Non-clustered index за Id
            modelBuilder.Entity<ApplicationUser>()
                .HasIndex(u => u.Id, "IX_Users_Id") // Дава име на индекса
                .IsUnique();

            modelBuilder.Entity<ApplicationRole>()
                .HasKey(r => r.N); // Казваме, че Primary Key е N

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

            base.OnModelCreating(modelBuilder);
        }
    }
}
