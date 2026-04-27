# Database Schema Documentation

**Database:** PostgreSQL  
**ORM:** Entity Framework Core 8 (Npgsql provider)  
**Project:** MyMvcApp

---

## Tables Overview

| Table | Primary Key | Description |
|---|---|---|
| `Users` | `Id` | All user accounts (Admin, Landlord, Tenant) |
| `Properties` | `PropertyId` | Rental property listings |
| `PropertyAmenities` | `AmenityId` | Amenities linked to a property |
| `Tenants` | `TenantId` | Active tenant lease records |
| `MaintenanceRequests` | `RequestId` | Maintenance jobs raised by tenants |
| `Payments` | `PaymentId` | Monthly rent payment records |
| `Documents` | `DocumentId` | Files uploaded to S3, linked to property/tenant |
| `VisitorPasses` | `VisitorPassId` | QR-code visitor passes issued by tenants |
| `CommunityUpdates` | `Id` | Announcements (standalone, no FKs) |
| `PasswordResetRequests` | `PasswordResetRequestId` | Admin-approved password reset requests |
| `AuditLogs` | `AuditLogId` | Audit trail of successful admin operations |

---

## Relationships & Foreign Keys

### AppUser (`Users`)
- **No foreign keys** — root table; all other user-linked tables point here.
- `Role` values: `Admin`, `Landlord`, `Tenant`

### Property (`Properties`)
| FK Column | References | On Delete |
|---|---|---|
| `LandlordId` | `Users.Id` | **Cascade** — delete landlord → delete their properties |

Navigation: `Landlord` (AppUser), `Tenant` (one-to-one), `Amenities`, `MaintenanceRequests`, `Payments`, `Documents`

### PropertyAmenity (`PropertyAmenities`)
| FK Column | References | On Delete |
|---|---|---|
| `PropertyId` | `Properties.PropertyId` | **Cascade** |

### Tenant (`Tenants`)
| FK Column | References | On Delete |
|---|---|---|
| `UserId` | `Users.Id` | **Cascade** — delete user → delete tenant record |
| `PropertyId` | `Properties.PropertyId` | **Restrict** — cannot delete property while tenant exists |

- `PropertyId` has a **unique index** enforcing the one-to-one constraint with `Property`.

### MaintenanceRequest (`MaintenanceRequests`)
| FK Column | References | On Delete |
|---|---|---|
| `TenantId` | `Tenants.TenantId` | **Cascade** |
| `PropertyId` | `Properties.PropertyId` | **Cascade** |

> Both FKs are NOT NULL — every request must belong to both a tenant and a property.  
> Delete order: tenant deleted → requests cascade deleted; then property can be deleted safely.

### Payment (`Payments`)
| FK Column | References | On Delete |
|---|---|---|
| `TenantId` | `Tenants.TenantId` | **Cascade** |
| `PropertyId` | `Properties.PropertyId` | **Cascade** |

> Same dual-FK pattern as MaintenanceRequest. PropertyId is a convenience FK for direct property-level queries.

### Document (`Documents`)
| FK Column | References | On Delete |
|---|---|---|
| `UploadedBy` | `Users.Id` | **Cascade** — delete user → delete their documents |
| `PropertyId` *(nullable)* | `Properties.PropertyId` | **ClientSetNull** — set to NULL, keep document record |
| `TenantId` *(nullable)* | `Tenants.TenantId` | **ClientSetNull** — set to NULL, keep document record |

> Documents survive property/tenant deletion with their S3 file intact but unlinked.

### VisitorPass (`VisitorPasses`)
| FK Column | References | On Delete |
|---|---|---|
| `TenantId` | `Tenants.TenantId` | **Cascade** |

### PasswordResetRequest (`PasswordResetRequests`)
| FK Column | References | On Delete |
|---|---|---|
| `AppUserId` *(nullable)* | `Users.Id` | **SetNull** — keep request log even if user deleted |

### CommunityUpdate (`CommunityUpdates`)
- **No foreign keys** — standalone announcements managed by CommunityAdmin.

### AuditLog (`AuditLogs`)
- **No foreign keys** — immutable admin activity log kept independently from user deletion.
- Stores the admin actor email, action name, target type, optional target id/email, details, and UTC timestamp.
- Indexed columns: `Action`, `ActorEmail`, `CreatedAt`

---

## Entity Relationship Diagram

```
Users (AppUser)
├── Properties (LandlordId → Users.Id)  [Cascade]
│   ├── PropertyAmenities (PropertyId)  [Cascade]
│   ├── Tenants (PropertyId, one-to-one) [Restrict]
│   │   ├── MaintenanceRequests (TenantId) [Cascade]
│   │   ├── Payments (TenantId)            [Cascade]
│   │   ├── Documents (TenantId, nullable) [ClientSetNull]
│   │   └── VisitorPasses (TenantId)       [Cascade]
│   ├── MaintenanceRequests (PropertyId)   [Cascade]
│   ├── Payments (PropertyId)              [Cascade]
│   └── Documents (PropertyId, nullable)   [ClientSetNull]
├── Tenants (UserId → Users.Id)            [Cascade]
├── Documents (UploadedBy → Users.Id)      [Cascade]
└── PasswordResetRequests (AppUserId, nullable) [SetNull]

CommunityUpdates  (standalone)
AuditLogs         (standalone admin audit trail)
```

---

## Enum Columns (stored as strings)

| Table | Column | Values |
|---|---|---|
| `Properties` | `PropertyType` | `Apartment`, `House`, `Condo`, `Studio`, `Commercial` |
| `Tenants` | `DepositStatus` | `Pending`, `Paid`, `Refunded` |
| `Tenants` | `LeaseStatus` | `Active`, `Expired`, `Terminated` |
| `MaintenanceRequests` | `Category` | `Plumbing`, `Electrical`, `AirConditioning`, `Structural`, `Appliances`, `PestControl`, `Others` |
| `MaintenanceRequests` | `Priority` | `High`, `Medium`, `Low` |
| `MaintenanceRequests` | `Status` | `Pending`, `Approved`, `InProgress`, `Completed`, `Rejected` |
| `Payments` | `PaymentMethod` | `OnlineTransfer`, `Cash`, `Cheque`, `DuitNow`, `Others` |
| `Payments` | `Status` | `Pending`, `Submitted`, `Verified`, `Overdue`, `Rejected` |
| `Documents` | `DocumentType` | `TenancyAgreement`, `IdentityCard`, `PaymentReceipt`, `InspectionReport`, `Others` |
| `VisitorPasses` | `Status` | `Active`, `Used`, `Expired`, `Cancelled` |
| `CommunityUpdates` | `Type` | `Event`, `Promotion`, `Notice` |
| `PasswordResetRequests` | `Status` | `Pending`, `Approved`, `Rejected` |

---

## Fixes Applied

| File | Issue | Fix |
|---|---|---|
| `Models/AppUser.cs` | Missing `[Key]` on `Id` | Added `[Key]` attribute |
| `Models/AppUser.cs` | `Email` uninitialized (nullable warning), no `[Required]` | Added `= string.Empty` and `[Required]` |
| `Models/AppUser.cs` | `Role` missing `[Required]` | Added `[Required]` attribute |
| `Data/AppDbContext.cs` | 10 FK relationships relied silently on EF Core conventions | All relationships now explicitly configured in `OnModelCreating` with correct `OnDelete` behaviors |
