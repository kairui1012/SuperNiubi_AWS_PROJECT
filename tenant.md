# Tenant Module Review
# 租客模块评估与企业级改进建议

## 1. Current Tenant Positioning

本项目的 Tenant 模块已经具备完整的租客自助服务门户雏形，不只是简单的 dashboard 页面。

目前 Tenant 可以完成：

- 查看租约与房屋信息
- 查看租金、押金、到期日
- 提交维修请求
- 上传租客文件
- 完成 mock payment（后续建议升级为 Stripe payment）
- 自动生成 payment receipt PDF
- 创建访客通行证
- 生成访客 QR code
- 查看系统公告
- 未分配物业时进入 PendingAssignment 页面

从目前代码完成度来看，Tenant 模块已经达到 **A-** 水平。  
如果要达到 A+ 企业级租客门户，还需要补齐 Stripe 支付流程、文件治理、访客通行证生命周期、租约操作历史和更完整的通知机制。

---

## 2. Current Tenant Features

### 2.1 Tenant Access Control

TenantController 使用 `[Authorize]` 保护，只有登录用户可以访问 Tenant 页面。

系统会根据当前登录用户 email 查询 `Tenants` 表：

- 如果 tenant 已经被分配 property，则进入正式 Tenant Dashboard
- 如果 tenant 尚未被分配 property，则进入 `PendingAssignment`
- 如果用户被 disabled，则不会进入正式 tenant 数据页面

相关文件：

- `MyMvcApp/Controllers/TenantController.cs`
- `MyMvcApp/Views/Tenant/PendingAssignment.cshtml`
- `MyMvcApp/Models/Tenant.cs`

完成状态：**已完成**

企业级评价：  
目前已经能防止未分配租客访问正式租客功能。  
但 Controller 级别目前主要使用 `[Authorize]`，建议进一步增加 `[Authorize(Roles = "Tenant")]`，让角色权限更清晰。

---

### 2.2 Tenant Dashboard

Tenant Dashboard 已经显示租客核心信息：

- Tenant email
- Property name
- Property address
- Lease start date
- Lease end date
- Lease status
- Monthly rent
- Next payment due date
- Payment record count
- Document quantity
- Visitor pass count
- Open maintenance count
- Maintenance status summary

相关文件：

- `MyMvcApp/Controllers/TenantController.cs`
- `MyMvcApp/Views/Tenant/TenantDashboard.cshtml`
- `MyMvcApp/Models/TenantDashboardViewModel.cs`

完成状态：**已完成**

企业级评价：  
Dashboard 已经覆盖租客日常最常用的信息，适合作为 tenant portal 的首页。

---

### 2.3 My Property

Tenant 可以查看自己被分配的物业和租约信息：

- Property details
- Property amenities
- Lease start date
- Lease end date
- Lease status
- Monthly rent
- Rent due day
- Deposit paid
- Deposit status

相关文件：

- `MyMvcApp/Views/Tenant/MyProperty.cshtml`
- `MyMvcApp/Models/TenantPropertyViewModel.cs`

完成状态：**已完成**

企业级评价：  
这部分已经能让租客清楚知道自己租住的物业、租约和押金情况。

---

### 2.4 Maintenance Request

Tenant 可以提交维修请求，并查看自己的维修记录：

- Title
- Category
- Priority
- Description
- Preferred date
- Status
- Created date

系统创建维修请求时会自动：

- 绑定当前 TenantId
- 绑定当前 PropertyId
- 设置 `Status = Pending`
- 保存 CreatedAt / UpdatedAt

相关文件：

- `MyMvcApp/Controllers/TenantController.cs`
- `MyMvcApp/Views/Tenant/MaintenanceRequest.cshtml`
- `MyMvcApp/Models/MaintenanceRequest.cs`
- `MyMvcApp/Models/MaintenanceRequestViewModel.cs`

完成状态：**已完成**

企业级评价：  
维修请求的基本闭环已经存在，因为 Landlord 端可以处理这些请求。  
如果要更企业级，需要补充图片上传、维修进度时间线、消息通知和关闭后反馈。

---

### 2.5 Documents

Tenant 可以上传和查看自己的文件：

- Tenancy agreement
- Identity card
- Payment receipt
- Inspection report
- Others

系统会把文件保存到：

- `wwwroot/uploads/tenant/{TenantId}/documents`

并在 `Documents` 表中保存：

- UploadedBy
- PropertyId
- TenantId
- DocumentName
- DocumentType
- FileKey
- FileSize
- FileType
- S3Url
- Notes

相关文件：

- `MyMvcApp/Controllers/TenantController.cs`
- `MyMvcApp/Views/Tenant/Documents.cshtml`
- `MyMvcApp/Models/Document.cs`
- `MyMvcApp/Models/TenantDocumentsViewModel.cs`

完成状态：**部分完成**

企业级评价：  
文件上传功能已经可用，但目前更像 local upload demo。  
如果项目重点是 AWS，建议后续改成真正的 S3 upload，并增加文件删除、下载权限、文件大小限制和文件类型限制。

---

### 2.6 Payments

Tenant 可以查看付款记录，并执行 mock payment。  
如果后续升级，建议改成使用 **Stripe payment**。

当前付款功能包括：

- 根据租约自动计算 next due date
- 自动计算当前应付月份
- 自动使用 monthly rent 作为付款金额
- 创建 Payment record
- 付款状态直接设为 `Verified`
- 自动生成 mock receipt PDF
- 保存 receipt file key

相关文件：

- `MyMvcApp/Controllers/TenantController.cs`
- `MyMvcApp/Views/Tenant/Payments.cshtml`
- `MyMvcApp/Models/Payment.cs`
- `MyMvcApp/Models/TenantPaymentsViewModel.cs`

完成状态：**部分完成**

企业级评价：  
这部分 demo 效果很好，因为租客可以看到付款记录，也能生成 receipt。  
但严格来说它还不是企业级支付系统，因为目前是 mock payment，并且付款会直接 verified。  
企业级版本建议使用 Stripe Checkout 或 Stripe PaymentIntent，并通过 Stripe webhook 更新本地 `Payment` 状态。

---

### 2.7 Visitor Pass

Tenant 可以创建访客通行证：

- Visitor name
- Visitor phone
- Purpose
- Visit date
- Notes
- Pass code
- QR payload
- QR code image

系统会自动生成：

- `VIS-` 开头的 pass code
- QR payload
- QR code data URL

相关文件：

- `MyMvcApp/Controllers/TenantController.cs`
- `MyMvcApp/Views/Tenant/Visitors.cshtml`
- `MyMvcApp/Models/VisitorPass.cs`
- `MyMvcApp/Models/TenantVisitorsViewModel.cs`

完成状态：**已完成**

企业级评价：  
这是 Tenant 模块的一个加分点，因为物业系统中访客管理非常实用。  
但目前还缺少 security guard scan / validate QR、pass expiry、pass cancel、used status 等完整生命周期。

---

### 2.8 Tenant Announcements

Tenant 可以查看 Admin 发布给 Tenant 或 All 的系统公告：

- VisibleTo = All
- VisibleTo = Tenant

相关文件：

- `MyMvcApp/Controllers/TenantController.cs`
- `MyMvcApp/Views/Tenant/Announcements.cshtml`
- `MyMvcApp/Models/SystemAnnouncement.cs`

完成状态：**已完成**

企业级评价：  
公告功能能让 Admin 与租客建立通知渠道，是企业级物业系统的重要组成部分。

---

## 3. Main Missing Feature For A+

### Stripe Payment Workflow Is Not Complete

Tenant 模块目前最大的问题是 payment 还没有接入 Stripe，还不是完整企业级线上付款流程。

当前系统是 mock payment：

- Tenant 点击付款后，系统直接创建 Payment
- Payment status 直接变成 `Verified`
- 系统自动生成 mock receipt PDF
- 没有 Stripe Checkout / PaymentIntent
- 没有 Stripe webhook
- 没有真实支付成功 / 失败 / 取消流程
- 没有 Stripe transaction id
- 没有 Stripe receipt URL

企业级判断：  
如果老师严格要求企业系统，payment 应该使用 Stripe 体现更真实的线上支付状态流：

Pending → Stripe Checkout Created → Paid / Failed / Cancelled

建议后续实现：

- Tenant 点击 Pay Now
- 系统创建 Stripe Checkout Session 或 PaymentIntent
- Tenant 跳转到 Stripe 完成付款
- Stripe webhook 通知系统付款成功或失败
- 成功后本地 Payment status 更新为 `Verified`
- 失败或取消后记录为 `Rejected` 或保留 `Pending`
- 保存 Stripe payment id / checkout session id / receipt url
- 付款成功后生成本地 receipt 或使用 Stripe receipt
- Stripe webhook 事件写入 audit log

当前状态：**Mock payment 已完成，Stripe payment 未完成**

优先级：**最高**

---

## 4. Enterprise-Level Gaps

### 4.1 Role-Level Authorization

目前 TenantController 使用 `[Authorize]`，并通过数据库查询当前 email 是否有 tenant assignment。

建议增强：

- Controller 或 action 增加 `[Authorize(Roles = "Tenant")]`
- 避免非 Tenant 角色访问 Tenant URL
- 对 PendingAssignment 保留合理例外

当前状态：**部分完成**

优先级：**高**

---

### 4.2 Stripe Payment Integration

建议新增：

- Stripe Checkout Session 或 PaymentIntent
- Stripe success / cancel return URL
- Stripe webhook endpoint
- Stripe payment id
- Stripe checkout session id
- Stripe receipt URL
- Payment status 自动同步
- Payment failed / cancelled handling
- Receipt only after successful Stripe payment

当前状态：**未完成**

优先级：**最高**

---

### 4.3 Document Governance

建议增强：

- 文件大小限制
- 文件类型白名单
- 文件删除 / soft delete
- 文件下载权限检查
- S3 upload
- Presigned URL
- Virus scan or safety validation

当前状态：**部分完成**

优先级：**中高**

---

### 4.4 Visitor Pass Lifecycle

建议增强：

- Cancel visitor pass
- Mark as used
- Expire old pass
- QR validation page
- Security guard scan workflow
- Visitor check-in / check-out time

当前状态：**部分完成**

优先级：**中**

---

### 4.5 Maintenance Experience

建议增强：

- Maintenance image upload
- Status timeline
- Landlord response display
- Completion confirmation by tenant
- Tenant rating / feedback after completion

当前状态：**部分完成**

优先级：**中**

---

### 4.6 Notifications

建议新增：

- Payment due reminder
- Maintenance status update notification
- Announcement notification
- Visitor pass created notification
- Lease expiry reminder

当前状态：**未完成**

优先级：**中**

---

## 5. Recommended A+ Tenant Roadmap

### Phase 1: Complete Stripe Payment Flow

必须优先完成：

1. Tenant clicks Pay Now
2. Backend creates Stripe Checkout Session or PaymentIntent
3. Tenant completes payment on Stripe
4. Stripe webhook updates local Payment status
5. Successful payment generates receipt
6. Failed / cancelled payment is handled clearly
7. Stripe payment audit log

---

### Phase 2: Improve Security And Governance

建议加入：

1. `[Authorize(Roles = "Tenant")]`
2. File size and file type validation
3. Secure download endpoint
4. S3 or presigned URL support
5. Tenant action audit logs

---

### Phase 3: Improve Daily Tenant Operations

建议加入：

1. Maintenance image upload
2. Maintenance timeline
3. Visitor pass cancel / expire / used
4. QR validation workflow
5. Notifications and reminders

---

## 6. A+ Standard Checklist

| Area | Current Status | A+ Requirement |
| --- | --- | --- |
| Tenant dashboard | Completed | Add richer status widgets |
| Pending assignment | Completed | Keep |
| My property | Completed | Keep |
| Lease details | Completed | Add lease history |
| Maintenance request | Completed | Add image upload and timeline |
| Documents | Partial | Add validation, delete, secure download, S3 |
| Payments | Partial | Replace mock verified flow with Stripe payment |
| Receipt PDF | Completed for mock payment | Generate only after successful Stripe payment |
| Visitor pass | Completed basic | Add QR validation and lifecycle |
| Announcements | Completed | Add notification/read status |
| Role security | Partial | Add `[Authorize(Roles = "Tenant")]` |
| Notifications | Missing | Add reminders and status updates |

---

## 7. Final Judgment

Tenant 模块目前已经完成了：

- Tenant dashboard
- Pending assignment handling
- My property / lease view
- Maintenance request submission
- Document upload
- Mock payment
- Receipt PDF generation
- Visitor pass creation
- QR code generation
- Tenant announcement view

但是，如果目标是企业级 A+，Tenant 模块还需要补：

1. Stripe payment flow
2. Tenant role-level authorization
3. 文件上传治理和安全下载
4. Visitor pass lifecycle 和 QR validation
5. Maintenance timeline 和图片上传
6. Payment / maintenance / announcement notification

建议最终目标：

> 把 Tenant 从 “自助查看页面” 升级成 “完整租客服务门户”。

如果补上 **Stripe Payment + Document Governance + Visitor Pass Lifecycle + Notifications**，Tenant 模块会更接近 A+ 标准。

---

## 8. Missing Features Summary

### Highest Priority

1. Stripe Checkout / PaymentIntent integration
2. Stripe webhook payment status sync
3. Stripe success / cancel handling
4. Tenant role authorization

### High Priority

1. File size/type validation
2. Secure document download
3. S3 document upload
4. Maintenance image upload
5. Audit log for tenant payment and document actions

### Medium Priority

1. Visitor pass cancel / expire / used
2. QR validation page
3. Maintenance timeline
4. Tenant feedback after maintenance completion
5. Payment due reminders

### AWS / Cloud Enhancement

1. Store documents in S3
2. Store receipts in S3 or use Stripe receipt URL
3. Use presigned URLs
4. Send payment reminders through email/SNS
5. Log Stripe payment webhook events for monitoring
