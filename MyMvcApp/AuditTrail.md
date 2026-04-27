# Audit Trail Feature

## Purpose

The Admin Console now includes an **Audit Trail** section in the sidebar. It helps admins review recent administrative activity, including who performed the action, what target was affected, when it happened, and a short detail message.

## Admin Sidebar

The sidebar contains a new **Audit Trail** item. Clicking it opens the audit pane without leaving the Admin Dashboard page.

## Tracked Actions

| Action | Trigger |
|---|---|
| `ApproveUser` | Admin approves a pending user registration |
| `DisableUser` | Admin disables a user account |
| `EnableUser` | Admin enables a disabled user account |
| `ChangeRole` | Admin changes a user's role |
| `ApprovePasswordReset` | Admin approves a password reset request |
| `RejectPasswordReset` | Admin rejects a password reset request |

## Database Table

Audit events are stored in the `AuditLogs` table.

| Column | Description |
|---|---|
| `AuditLogId` | Primary key |
| `Action` | Admin action name |
| `ActorEmail` | Email of the admin who performed the action |
| `TargetType` | Target record type, such as `User` or `PasswordResetRequest` |
| `TargetId` | Optional target record id |
| `TargetEmail` | Optional target email |
| `Details` | Short human-readable action detail |
| `CreatedAt` | UTC timestamp |

## Admin Dashboard Display

The Audit Trail pane shows:

- Total audit events
- Events from the last 24 hours
- User management event count
- Password reset event count
- Latest 50 audit records

## Files Changed

| File | Change |
|---|---|
| `Models/AuditLog.cs` | Added the audit log entity |
| `Data/AppDbContext.cs` | Added `AuditLogs` DbSet and indexes |
| `Controllers/AdminController.cs` | Writes audit logs after successful admin operations |
| `Models/Admin/AdminDashboardViewModel.cs` | Added audit summary and audit row view models |
| `Views/Admin/Admin.cshtml` | Added Audit Trail sidebar item and pane |
| `wwwroot/css/pages/admin.css` | Added audit table styling |
| `Migrations/20260427030000_AddAuditLogs.cs` | Creates the `AuditLogs` table |
| `database.md` | Updated database schema documentation |
