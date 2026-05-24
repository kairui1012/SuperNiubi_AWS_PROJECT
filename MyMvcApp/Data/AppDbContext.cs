using Microsoft.EntityFrameworkCore;
using MyMvcApp.Models;


namespace MyMvcApp.Data
{
    /// <summary>
    /// Represents the Entity Framework database context for the property management application.
    /// </summary>
    public class AppDbContext : DbContext
    {
        /// <summary>
        /// Creates a database context using the configured Entity Framework options.
        /// </summary>
        /// <param name="options">The database provider and connection options.</param>
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        /// <summary>
        /// Application users, including tenants, landlords, security users, and admins.
        /// </summary>
        public DbSet<AppUser> Users { get; set; }

        /// <summary>
        /// Rental properties managed by landlords and admins.
        /// </summary>
        public DbSet<Property> Properties { get; set; }

        /// <summary>
        /// Amenities linked to rental properties.
        /// </summary>
        public DbSet<PropertyAmenity> PropertyAmenities { get; set; }

        /// <summary>
        /// Tenant lease and occupancy records.
        /// </summary>
        public DbSet<Tenant> Tenants { get; set; }

        /// <summary>
        /// Maintenance requests submitted for tenant properties.
        /// </summary>
        public DbSet<MaintenanceRequest> MaintenanceRequests { get; set; }

        /// <summary>
        /// Timeline events that track maintenance request progress.
        /// </summary>
        public DbSet<MaintenanceTimeline> MaintenanceTimelines { get; set; }

        /// <summary>
        /// Tenant payment records and payment gateway identifiers.
        /// </summary>
        public DbSet<Payment> Payments { get; set; }

        /// <summary>
        /// Uploaded files and document metadata.
        /// </summary>
        public DbSet<Document> Documents { get; set; }

        /// <summary>
        /// Public community updates shown on the landing page.
        /// </summary>
        public DbSet<CommunityUpdate> CommunityUpdates { get; set; }

        /// <summary>
        /// Visitor passes created by tenants for guest access.
        /// </summary>
        public DbSet<VisitorPass> VisitorPasses { get; set; }

        /// <summary>
        /// Password reset requests that require admin review.
        /// </summary>
        public DbSet<PasswordResetRequest> PasswordResetRequests { get; set; }

        /// <summary>
        /// Administrative audit log entries.
        /// </summary>
        public DbSet<AuditLog> AuditLogs { get; set; }

        /// <summary>
        /// System announcements displayed to selected user roles.
        /// </summary>
        public DbSet<SystemAnnouncement> SystemAnnouncements { get; set; }

        /// <summary>
        /// Historical lease changes for tenant records.
        /// </summary>
        public DbSet<LeaseHistory> LeaseHistories { get; set; }

        /// <summary>
        /// Property booking records and booking payment states.
        /// </summary>
        public DbSet<PropertyBooking> PropertyBookings { get; set; }

        /// <summary>
        /// Promotional codes that can be applied to property bookings.
        /// </summary>
        public DbSet<PromoCode> PromoCodes { get; set; }


        /// <summary>
        /// Configures entity conversions, indexes, relationships, and delete behaviors.
        /// </summary>
        /// <param name="modelBuilder">The builder used to configure the EF Core model.</param>
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
            modelBuilder.Entity<Document>().Property(d => d.UploadStatus).HasConversion<string>();
            modelBuilder.Entity<Document>().HasIndex(d => d.FileKey);
            modelBuilder.Entity<Document>().HasIndex(d => d.UploadId);
            modelBuilder.Entity<Document>().HasIndex(d => d.UploadStatus);
            modelBuilder.Entity<VisitorPass>().Property(v => v.Status).HasConversion<string>();
            modelBuilder.Entity<PasswordResetRequest>().Property(p => p.Status).HasConversion<string>();
            modelBuilder.Entity<PropertyBooking>().Property(p => p.Status).HasConversion<string>();
            modelBuilder.Entity<PropertyBooking>().Property(p => p.PaymentStatus).HasConversion<string>();

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

            // --- Property -> Tenant lease records ---
            // A property can have multiple historical leases; business logic treats only Active leases as occupied.
            modelBuilder.Entity<Tenant>()
                .HasOne(t => t.Property)
                .WithMany(p => p.Tenants)
                .HasForeignKey(t => t.PropertyId)
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
            modelBuilder.Entity<PropertyBooking>()
                .HasOne(b => b.Property)
                .WithMany()
                .HasForeignKey(b => b.PropertyId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PropertyBooking>()
                .HasOne(b => b.PromoCode)
                .WithMany()
                .HasForeignKey(b => b.PromoCodeId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
