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
        public DbSet<MaintenanceTimeline> MaintenanceTimelines { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<Document> Documents { get; set; }
        public DbSet<CommunityUpdate> CommunityUpdates { get; set; }
        public DbSet<VisitorPass> VisitorPasses { get; set; }
        public DbSet<PasswordResetRequest> PasswordResetRequests { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<SystemAnnouncement> SystemAnnouncements { get; set; }
        public DbSet<LeaseHistory> LeaseHistories { get; set; }

        public DbSet<Facility> Facilities { get; set; }
        public DbSet<FacilityBooking> FacilityBookings { get; set; }
        public DbSet<PromoCode> PromoCodes { get; set; }


        // Setting rules to store the data
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Store ENUM as String for easier check, if not it will be 1, 2, 3 in db
            modelBuilder.Entity<Property>().Property(p => p.PropertyType).HasConversion<string>();
            modelBuilder.Entity<Property>().Property(p => p.AvailabilityStatus).HasConversion<string>();
            modelBuilder.Entity<Property>().Property(p => p.ApprovalStatus).HasConversion<string>();
            modelBuilder.Entity<CommunityUpdate>().Property(c => c.Type).HasConversion<string>();
            modelBuilder.Entity<Tenant>().Property(t => t.DepositStatus).HasConversion<string>();
            modelBuilder.Entity<Tenant>().Property(t => t.LeaseStatus).HasConversion<string>();
            modelBuilder.Entity<MaintenanceRequest>().Property(m => m.Category).HasConversion<string>();
            modelBuilder.Entity<MaintenanceRequest>().Property(m => m.Priority).HasConversion<string>();
            modelBuilder.Entity<MaintenanceRequest>().Property(m => m.Status).HasConversion<string>();
            modelBuilder.Entity<Payment>().Property(p => p.PaymentMethod).HasConversion<string>();
            modelBuilder.Entity<Payment>().Property(p => p.Status).HasConversion<string>();
            modelBuilder.Entity<Payment>().HasIndex(p => p.StripeSessionId);
            modelBuilder.Entity<Payment>().HasIndex(p => p.StripePaymentIntentId);
            modelBuilder.Entity<Payment>().HasIndex(p => p.StripeEventId);
            modelBuilder.Entity<Document>().Property(d => d.DocumentType).HasConversion<string>();
            modelBuilder.Entity<VisitorPass>().Property(v => v.Status).HasConversion<string>();
            modelBuilder.Entity<PasswordResetRequest>().Property(p => p.Status).HasConversion<string>();
            modelBuilder.Entity<FacilityBooking>().Property(f => f.Status).HasConversion<string>();
            modelBuilder.Entity<FacilityBooking>().Property(f => f.PaymentStatus).HasConversion<string>();

            modelBuilder.Entity<AppUser>()
                .HasIndex(u => u.CreatedAt);

            modelBuilder.Entity<Property>()
                .HasIndex(p => p.IsDeleted);

            modelBuilder.Entity<Property>()
                .HasIndex(p => p.ApprovalStatus);

            modelBuilder.Entity<Property>()
                .HasIndex(p => p.AvailabilityStatus);

            modelBuilder.Entity<AuditLog>()
                .HasIndex(a => a.CreatedAt);

            modelBuilder.Entity<AuditLog>()
                .HasIndex(a => a.ActorEmail);

            modelBuilder.Entity<AuditLog>()
                .HasIndex(a => a.Action);

            modelBuilder.Entity<SystemAnnouncement>()
                .HasIndex(a => a.CreatedAt);

            modelBuilder.Entity<SystemAnnouncement>()
                .HasIndex(a => a.VisibleTo);

            modelBuilder.Entity<LeaseHistory>()
                .HasIndex(h => h.TenantId);

            modelBuilder.Entity<LeaseHistory>()
                .HasIndex(h => h.CreatedAt);

            modelBuilder.Entity<LeaseHistory>()
                .HasIndex(h => h.Action);

            modelBuilder.Entity<MaintenanceTimeline>()
                .HasIndex(t => t.RequestId);

            modelBuilder.Entity<MaintenanceTimeline>()
                .HasIndex(t => t.CreatedAt);

            // --- AppUser (Landlord) → Property ---
            // Deleting a landlord cascades to their properties
            modelBuilder.Entity<Property>()
                .HasOne(p => p.Landlord)
                .WithMany()
                .HasForeignKey(p => p.LandlordId)
                .OnDelete(DeleteBehavior.Cascade);

            // --- AppUser → Tenant ---
            // Deleting a user account cascades to their tenant record
            modelBuilder.Entity<Tenant>()
                .HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<LeaseHistory>()
                .HasOne(h => h.Tenant)
                .WithMany(t => t.LeaseHistories)
                .HasForeignKey(h => h.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            // --- Property ↔ Tenant (one-to-one) ---
            // Cannot delete a property while a tenant is assigned to it
            modelBuilder.Entity<Tenant>()
                .HasOne(t => t.Property)
                .WithOne(p => p.Tenant)
                .HasForeignKey<Tenant>(t => t.PropertyId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- Property → PropertyAmenity ---
            modelBuilder.Entity<PropertyAmenity>()
                .HasOne(a => a.Property)
                .WithMany(p => p.Amenities)
                .HasForeignKey(a => a.PropertyId)
                .OnDelete(DeleteBehavior.Cascade);

            // --- Tenant → MaintenanceRequest ---
            // Deleting tenant cascades; property side also cascades (safe in PostgreSQL)
            modelBuilder.Entity<MaintenanceRequest>()
                .HasOne(m => m.Tenant)
                .WithMany(t => t.MaintenanceRequests)
                .HasForeignKey(m => m.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<MaintenanceRequest>()
                .HasOne(m => m.Property)
                .WithMany(p => p.MaintenanceRequests)
                .HasForeignKey(m => m.PropertyId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<MaintenanceTimeline>()
                .HasOne(t => t.MaintenanceRequest)
                .WithMany(r => r.Timeline)
                .HasForeignKey(t => t.RequestId)
                .OnDelete(DeleteBehavior.Cascade);

            // --- Tenant → Payment ---
            modelBuilder.Entity<Payment>()
                .HasOne(pay => pay.Tenant)
                .WithMany(t => t.Payments)
                .HasForeignKey(pay => pay.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Payment>()
                .HasOne(pay => pay.Property)
                .WithMany(p => p.Payments)
                .HasForeignKey(pay => pay.PropertyId)
                .OnDelete(DeleteBehavior.Cascade);

            // --- Document FKs ---
            // Uploader deleted → delete document
            modelBuilder.Entity<Document>()
                .HasOne(d => d.UploadedByUser)
                .WithMany()
                .HasForeignKey(d => d.UploadedBy)
                .OnDelete(DeleteBehavior.Cascade);

            // Property/Tenant deleted → null out the optional FK (keep document record)
            modelBuilder.Entity<Document>()
                .HasOne(d => d.Property)
                .WithMany(p => p.Documents)
                .HasForeignKey(d => d.PropertyId)
                .OnDelete(DeleteBehavior.ClientSetNull);

            modelBuilder.Entity<Document>()
                .HasOne(d => d.Tenant)
                .WithMany(t => t.Documents)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull);

            // --- Tenant → VisitorPass ---
            modelBuilder.Entity<VisitorPass>()
                .HasOne(v => v.Tenant)
                .WithMany(t => t.VisitorPasses)
                .HasForeignKey(v => v.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            // --- PasswordResetRequest → AppUser (optional link) ---
            modelBuilder.Entity<PasswordResetRequest>()
                .HasOne(p => p.AppUser)
                .WithMany()
                .HasForeignKey(p => p.AppUserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<FacilityBooking>()
                .HasOne(b => b.Facility)
                .WithMany(f => f.Bookings)
                .HasForeignKey(b => b.FacilityId)
                .OnDelete(DeleteBehavior.Cascade);

            // --- FacilityBooking -> AppUser ---
            // If a user is deleted, we keep the booking for financial records but null out the User ID
            modelBuilder.Entity<FacilityBooking>()
                .HasOne(b => b.AppUser)
                .WithMany()
                .HasForeignKey(b => b.AppUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // --- FacilityBooking -> PromoCode ---
            // If a promo code is deleted, null it out on the booking record
            modelBuilder.Entity<FacilityBooking>()
                .HasOne(b => b.PromoCode)
                .WithMany()
                .HasForeignKey(b => b.PromoCodeId)
                .OnDelete(DeleteBehavior.SetNull);

        }
    }
}
