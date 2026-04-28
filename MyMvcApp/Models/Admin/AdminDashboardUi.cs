namespace MyMvcApp.Models.Admin
{
    public static class AdminDashboardUi
    {
        public static string GetUserStatusLabel(AppUser user)
        {
            if (user.IsDisabled)
            {
                return "Disabled";
            }

            return user.IsApproved ? "Approved" : "Pending";
        }

        public static string GetUserStatusBadgeClass(AppUser user)
        {
            if (user.IsDisabled)
            {
                return "badge-soft-danger";
            }

            return user.IsApproved ? "badge-soft-success" : "badge-soft-warning";
        }

        public static string GetMaintenanceBadgeClass(MaintenanceStatus status)
        {
            return status switch
            {
                MaintenanceStatus.Pending => "badge-soft-warning",
                MaintenanceStatus.Approved => "badge-soft-info",
                MaintenanceStatus.InProgress => "badge-soft-primary",
                MaintenanceStatus.Completed => "badge-soft-success",
                _ => "badge-soft-danger"
            };
        }

        public static string GetPaymentBadgeClass(PaymentStatus status)
        {
            return status switch
            {
                PaymentStatus.Pending => "badge-soft-secondary",
                PaymentStatus.Submitted => "badge-soft-info",
                PaymentStatus.Verified => "badge-soft-success",
                PaymentStatus.Overdue => "badge-soft-warning",
                PaymentStatus.Cancelled => "badge-soft-warning",
                PaymentStatus.Refunded => "badge-soft-secondary",
                _ => "badge-soft-danger"
            };
        }

        public static string GetPriorityBadgeClass(MaintenancePriority priority)
        {
            return priority switch
            {
                MaintenancePriority.High => "badge-soft-danger",
                MaintenancePriority.Medium => "badge-soft-warning",
                _ => "badge-soft-secondary"
            };
        }

        public static string GetAuditActionBadgeClass(string action)
        {
            return action switch
            {
                "ApproveUser" or "EnableUser" or "ApprovePasswordReset" => "badge-soft-success",
                "DisableUser" or "RejectPasswordReset" => "badge-soft-danger",
                "ChangeRole" => "badge-soft-info",
                "CreateAnnouncement" => "badge-soft-primary",
                "EditAnnouncement" => "badge-soft-warning",
                "DeleteAnnouncement" => "badge-soft-danger",
                _ => "badge-soft-secondary"
            };
        }
    }
}
