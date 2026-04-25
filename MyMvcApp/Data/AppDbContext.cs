using Microsoft.EntityFrameworkCore;
using MyMvcApp.Models;


namespace MyMvcApp.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        
        public DbSet<AppUser> Users { get; set; }
        public DbSet<Property> Properties { get; set; }
        public DbSet<PropertyAmenity> PropertyAmenities { get; set; }
        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<MaintenanceRequest> MaintenanceRequests { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<Document> Documents { get; set; }
        public DbSet<CommunityUpdate> CommunityUpdates { get; set; }

        // Setting rules to store the data
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Store ENUM as String for easier check, if not it will be 1, 2, 3 in db
            modelBuilder.Entity<Property>().Property(p => p.PropertyType).HasConversion<string>();
            modelBuilder.Entity<CommunityUpdate>().Property(c => c.Type).HasConversion<string>();
            modelBuilder.Entity<Tenant>().Property(t => t.DepositStatus).HasConversion<string>();
            modelBuilder.Entity<Tenant>().Property(t => t.LeaseStatus).HasConversion<string>();
            modelBuilder.Entity<MaintenanceRequest>().Property(m => m.Category).HasConversion<string>();
            modelBuilder.Entity<MaintenanceRequest>().Property(m => m.Priority).HasConversion<string>();
            modelBuilder.Entity<MaintenanceRequest>().Property(m => m.Status).HasConversion<string>();
            modelBuilder.Entity<Payment>().Property(p => p.PaymentMethod).HasConversion<string>();
            modelBuilder.Entity<Payment>().Property(p => p.Status).HasConversion<string>();
            modelBuilder.Entity<Document>().Property(d => d.DocumentType).HasConversion<string>();

            // Avoid Cascade Delete Conflict
            // Logic: One property has only one tenant, one tenant has only one  property
            modelBuilder.Entity<Tenant>()
                .HasOne(t => t.Property)
                .WithOne(p => p.Tenant)
                .HasForeignKey<Tenant>(t => t.PropertyId)
                .OnDelete(DeleteBehavior.Restrict);

        }
    }
}

