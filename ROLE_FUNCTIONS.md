# Role Functions

This document summarizes what each user role can do in the current PropEase MVC application, based on the controllers, role checks, and dashboard flows in `MyMvcApp`.

## Role Source

Roles are stored on `AppUser.Role` and stamped into ASP.NET claims by `RoleClaimsTransformation`.

Known roles:

- `Admin`
- `Landlord`
- `Tenant`
- `Security`

New registered users are created as `Tenant` by default and start as not approved.

## Public / Guest

Users without login can access public property and auth flows.

Main functions:

- View homepage and public property details.
- Register an account.
- Login and logout.
- Request password reset approval.
- View pending approval and access denied pages.
- Browse short-term stay listings.
- Book a short-term stay through Stripe Checkout.
- View booking success or cancel pages.
- Verify short-term property access pass by code.

Main code areas:

- `AccountController`
- `HomeController`
- `PropertyBookingController`
- `PropertyGuardController`

## Tenant

Tenant is the default registered role. After admin approval and property assignment, tenant users can manage their own rental experience.

Main functions:

- Access tenant dashboard.
- View assigned property, lease dates, rent amount, deposit status, and amenities.
- View dashboard notifications for rent, lease expiry, maintenance, visitors, and announcements.
- Submit maintenance requests with optional issue image.
- View maintenance request history and timeline.
- Confirm completed maintenance work and optionally leave rating or feedback.
- Upload tenant documents to S3.
- View, download, and archive their own documents.
- View rent payment records.
- Start Stripe Checkout for the next unpaid rent period.
- View rent payment success or cancellation state.
- Create visitor passes with QR payloads.
- View visitor pass QR codes.
- Cancel active visitor passes.
- Mark active visitor passes as used.
- View announcements visible to `All` or `Tenant`.
- If no tenant record/property assignment exists, see pending assignment page.

Main code areas:

- `TenantController.Dashboard`
- `TenantController.TenantDashboard`
- `TenantController.PendingAssignment`
- `TenantController.MyProperty`
- `TenantController.MaintenanceRequest`
- `TenantController.CreateMaintenance`
- `TenantController.ConfirmMaintenanceCompletion`
- `TenantController.Documents`
- `TenantController.UploadDocument`
- `TenantController.DownloadDocument`
- `TenantController.DeleteDocument`
- `TenantController.Payments`
- `TenantController.CreateCheckoutSession`
- `TenantController.Visitors`
- `TenantController.RegisterVisitor`
- `TenantController.CancelVisitorPass`
- `TenantController.MarkVisitorPassUsed`
- `TenantController.Announcements`

Access notes:

- `TenantController` has controller-level `[Authorize]`.
- `Announcements` is explicitly `[Authorize(Roles = "Tenant")]`.
- Many tenant actions validate access by matching the logged-in email to a `Tenant` record.

## Landlord

Landlord users manage their own properties, tenants, documents, payments, maintenance, and landlord announcements.

Main functions:

- Access landlord dashboard with property, tenant, income, vacancy, payment, and maintenance summary.
- View own properties.
- View property details.
- Add a property with image and amenities.
- Edit own properties.
- Soft-delete own properties.
- Submit properties for admin approval.
- View tenants assigned to their properties.
- View tenant details.
- Renew lease.
- Terminate lease.
- Adjust rent and rent due day.
- Move tenant to another landlord-owned property.
- Change tenant deposit status.
- Assign approved tenant users to landlord-owned properties.
- View maintenance requests for landlord-owned properties.
- Update maintenance priority, status, remarks, vendor, estimated cost, and repair image.
- Send tenant notification email when maintenance status changes.
- View payments for landlord-owned properties.
- Upload landlord documents linked to a property or tenant.
- View, download, and archive landlord-managed documents.
- View announcements visible to `All`, `Landlord`, or created by the landlord.
- Create landlord announcements for `All`, `Tenant`, or `Landlord`.

Main code areas:

- `LandlordController.Dashboard`
- `LandlordController.MyProperties`
- `LandlordController.PropertyDetails`
- `LandlordController.AddProperty`
- `LandlordController.EditProperty`
- `LandlordController.DeleteProperty`
- `LandlordController.Tenants`
- `LandlordController.TenantDetails`
- `LandlordController.RenewLease`
- `LandlordController.TerminateLease`
- `LandlordController.AdjustRent`
- `LandlordController.ChangeTenantProperty`
- `LandlordController.ChangeDepositStatus`
- `LandlordController.AssignTenant`
- `LandlordController.MaintenanceRequests`
- `LandlordController.EditMaintenanceRequest`
- `LandlordController.Payments`
- `LandlordController.Documents`
- `LandlordController.UploadDocument`
- `LandlordController.DownloadDocument`
- `LandlordController.DeleteDocument`
- `LandlordController.Announcements`
- `LandlordController.CreateAnnouncement`

Access notes:

- `LandlordController` has controller-level `[Authorize]`.
- Several actions internally verify the current user exists with `Role == "Landlord"`.
- `Announcements` and `CreateAnnouncement` are explicitly `[Authorize(Roles = "Landlord")]`.

## Admin

Admin users operate the system-level management dashboard.

Main functions:

- Access admin dashboard.
- View system analytics:
  - collection trend
  - new users
  - maintenance request trend
  - user role composition
  - property occupancy
  - payment and maintenance totals
- Search and filter users by email, role, and status.
- Approve pending users.
- Disable users.
- Enable users.
- Change user role among `Tenant`, `Landlord`, `Security`, and `Admin`.
- Approve password reset requests and trigger Cognito reset email.
- Reject password reset requests.
- View property directory.
- Search properties.
- Approve landlord-submitted properties.
- Reject landlord-submitted properties.
- View and filter maintenance queue.
- View payment monitoring data.
- Verify payments.
- Reject payments.
- Export payment report CSV.
- View audit logs.
- Filter audit logs by search, action, and date range.
- Create system announcements.
- Edit system announcements.
- Delete system announcements.
- Manage community updates with images:
  - list updates
  - create updates
  - edit updates
  - delete updates

Main code areas:

- `AdminController`
- `AdminPaymentController`
- `CommunityAdminController`

Access notes:

- `AdminController` is `[Authorize(Roles = "Admin")]`.
- `AdminPaymentController` is `[Authorize(Roles = "Admin")]`.
- `CommunityAdminController` is `[Authorize(Roles = "Admin")]`.
- Admin role changes protect the current admin from disabling self or removing own admin role.

## Security

Security users are routed directly to visitor pass validation after login.

Main functions:

- Validate visitor pass by manual code or QR payload.
- See whether visitor pass is found, active, expired, cancelled, used, or invalid.
- Check in a valid active visitor pass, marking it as `Used`.

Main code areas:

- `TenantController.ValidateVisitorPass`
- `TenantController.ValidateVisitorPassAndCheckIn`

Access notes:

- These two actions are explicitly `[Authorize(Roles = "Security")]`.
- `AccountController.Login` redirects `Security` users to `TenantController.ValidateVisitorPass`.

## Important Implementation Notes

- Role claims are added from the database by `RoleClaimsTransformation`, not directly from Cognito groups.
- User login is blocked when `AppUser.IsDisabled` is true.
- User login is blocked until `AppUser.IsApproved` is true.
- Registration creates users as unapproved `Tenant`.
- Some role boundaries are enforced by controller attributes, while others are enforced inside action logic by checking the current email and role-specific records.
- `PropertyBookingController` and `PropertyGuardController` are public flows for short-term guests, not tied to a logged-in role.
