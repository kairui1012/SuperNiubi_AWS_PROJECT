using Microsoft.EntityFrameworkCore;

namespace MyMvcApp.Serverless
{
    public enum PaymentStatus { Pending, Submitted, Verified, Overdue, Rejected, Failed, Cancelled, Refunded }
    public enum BookingStatus { Pending, Confirmed, Cancelled }
    public enum BookingPaymentStatus { Pending, Paid, Failed }
    public enum PaymentMethod { OnlineTransfer, Cash, Cheque, DuitNow, Others }
    public enum ReportExportStatus { Pending, Processing, Completed, Failed }

    public class StripeWorkerDbContext : DbContext
    {
        public StripeWorkerDbContext(DbContextOptions<StripeWorkerDbContext> options) : base(options) { }

        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<PropertyBooking> PropertyBookings => Set<PropertyBooking>();
        public DbSet<Property> Properties => Set<Property>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<ReportExportJob> ReportExportJobs => Set<ReportExportJob>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Payment>().Property(p => p.Status).HasConversion<string>();
            modelBuilder.Entity<Payment>().Property(p => p.PaymentMethod).HasConversion<string>();
            modelBuilder.Entity<Payment>().HasIndex(p => p.StripeSessionId);
            modelBuilder.Entity<Payment>().HasIndex(p => p.StripePaymentIntentId);
            modelBuilder.Entity<Payment>().HasIndex(p => p.StripeEventId);
            modelBuilder.Entity<PropertyBooking>().Property(p => p.Status).HasConversion<string>();
            modelBuilder.Entity<PropertyBooking>().Property(p => p.PaymentStatus).HasConversion<string>();
            modelBuilder.Entity<ReportExportJob>().Property(r => r.Status).HasConversion<string>();
            modelBuilder.Entity<PropertyBooking>()
                .HasOne(b => b.Property)
                .WithMany()
                .HasForeignKey(b => b.PropertyId);
            modelBuilder.Entity<Payment>()
                .HasOne(p => p.Tenant)
                .WithMany()
                .HasForeignKey(p => p.TenantId);
            modelBuilder.Entity<Payment>()
                .HasOne(p => p.Property)
                .WithMany()
                .HasForeignKey(p => p.PropertyId);
            modelBuilder.Entity<Tenant>()
                .HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId);
            modelBuilder.Entity<Property>()
                .HasOne(p => p.Landlord)
                .WithMany()
                .HasForeignKey(p => p.LandlordId);
        }
    }

    public class Payment
    {
        public int PaymentId { get; set; }
        public int TenantId { get; set; }
        public int PropertyId { get; set; }
        public string PaymentMonth { get; set; } = string.Empty;
        public int PaymentYear { get; set; }
        public decimal Amount { get; set; }
        public DateTime? PaymentDate { get; set; }
        public DateTime DueDate { get; set; }
        public PaymentMethod? PaymentMethod { get; set; }
        public string? ReferenceNo { get; set; }
        public string? StripeSessionId { get; set; }
        public string? StripePaymentIntentId { get; set; }
        public string? StripeReceiptUrl { get; set; }
        public string? StripeEventId { get; set; }
        public string? StripeRefundId { get; set; }
        public decimal? RefundAmount { get; set; }
        public DateTime? RefundDate { get; set; }
        public string? RefundReason { get; set; }
        public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
        public string? LandlordRemarks { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public Tenant Tenant { get; set; } = null!;
        public Property Property { get; set; } = null!;
    }

    public class Tenant
    {
        public int TenantId { get; set; }
        public int UserId { get; set; }
        public int PropertyId { get; set; }
        public AppUser User { get; set; } = null!;
    }

    public class PropertyBooking
    {
        public int Id { get; set; }
        public int PropertyId { get; set; }
        public string GuestName { get; set; } = string.Empty;
        public string GuestEmail { get; set; } = string.Empty;
        public DateTime CheckInDate { get; set; }
        public DateTime CheckOutDate { get; set; }
        public BookingStatus Status { get; set; } = BookingStatus.Pending;
        public BookingPaymentStatus PaymentStatus { get; set; } = BookingPaymentStatus.Pending;
        public string? StripeSessionId { get; set; }
        public string? StripePaymentIntentId { get; set; }
        public string? PassCode { get; set; }
        public Property Property { get; set; } = null!;
    }

    public class Property
    {
        public int PropertyId { get; set; }
        public int LandlordId { get; set; }
        public string PropertyName { get; set; } = string.Empty;
        public string AddressLine1 { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public AppUser? Landlord { get; set; }
    }

    public class AppUser
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
    }

    public class AuditLog
    {
        public int AuditLogId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string ActorEmail { get; set; } = string.Empty;
        public string TargetType { get; set; } = string.Empty;
        public int? TargetId { get; set; }
        public string? Details { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ReportExportJob
    {
        public int ReportExportJobId { get; set; }
        public string ReportType { get; set; } = "PaymentCsv";
        public string RequestedByEmail { get; set; } = string.Empty;
        public ReportExportStatus Status { get; set; } = ReportExportStatus.Pending;
        public string? FilterJson { get; set; }
        public string? S3Bucket { get; set; }
        public string? S3Key { get; set; }
        public string? FileName { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
