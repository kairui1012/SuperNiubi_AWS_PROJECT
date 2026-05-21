# Project Database and AWS Usage Overview

本文档整理当前项目里看到的数据库、table、S3、RDS 以及相关 AWS 服务使用情况。

## 1. 数据库类型

项目使用的是 PostgreSQL。

证据：

- `MyMvcApp/Program.cs` 使用 `UseNpgsql(...)` 注册 EF Core 数据库连接。
- `MyMvcApp/MyMvcApp.csproj` 引用了 `Npgsql.EntityFrameworkCore.PostgreSQL`。
- `MyMvcApp.Serverless/Function.cs` 的 Lambda worker 也使用 `UseNpgsql(...)`。
- migrations 里使用 `Npgsql.EntityFrameworkCore.PostgreSQL.Metadata`。

连接字符串 key：

```json
"ConnectionStrings": {
  "DefaultConnection": ""
}
```

实际部署时应该通过 `ConnectionStrings__DefaultConnection` 或环境变量注入真实连接字符串。当前 `appsettings.json` 和 `appsettings.Development.json` 中 `DefaultConnection` 是空字符串。

## 2. RDS 使用情况

代码没有直接写死 `RDS` 字样，也没有在 repository 里看到 RDS endpoint。

但是因为项目使用 PostgreSQL，并且是 AWS 部署语境，`DefaultConnection` 很可能连接到 Amazon RDS for PostgreSQL。也就是说：

- 应用层使用的是 PostgreSQL。
- 如果部署到 AWS，RDS 应该是承载 PostgreSQL 的托管数据库服务。
- 代码本身只依赖连接字符串，不关心 PostgreSQL 是本机、Docker、EC2 上自建，还是 Amazon RDS。

需要在 AWS Console 或部署环境变量里确认真实 RDS endpoint。

## 3. 当前 EF Core DbContext Tables

主应用 `MyMvcApp/Data/AppDbContext.cs` 当前映射的 DbSet/table 如下：

| Table | Model | 用途 |
| --- | --- | --- |
| `Users` | `AppUser` | 系统用户，包含 landlord、tenant、admin 等角色基础资料 |
| `Properties` | `Property` | 房产/单位资料 |
| `PropertyAmenities` | `PropertyAmenity` | 房产设施/amenity |
| `Tenants` | `Tenant` | 租户资料、租约状态、押金状态等 |
| `MaintenanceRequests` | `MaintenanceRequest` | 租户维修请求 |
| `MaintenanceTimelines` | `MaintenanceTimeline` | 维修请求处理时间线 |
| `Payments` | `Payment` | 租金/付款记录，含 Stripe 字段 |
| `Documents` | `Document` | 租户/房东上传文件记录，含 S3 file key 和 URL |
| `CommunityUpdates` | `CommunityUpdate` | 社区公告/更新 |
| `VisitorPasses` | `VisitorPass` | 访客通行证 |
| `PasswordResetRequests` | `PasswordResetRequest` | 密码重置请求 |
| `AuditLogs` | `AuditLog` | 管理员/系统操作审计日志 |
| `SystemAnnouncements` | `SystemAnnouncement` | 系统公告 |
| `LeaseHistories` | `LeaseHistory` | 租约历史记录 |
| `PropertyBookings` | `PropertyBooking` | 短租/房产预订 |
| `PromoCodes` | `PromoCode` | 优惠码 |

另外，migration 历史里出现过：

| Table | 状态 |
| --- | --- |
| `Facilities` | migration 中出现过，但当前 `AppDbContext` 没有 `DbSet<Facility>` |
| `FacilityBookings` | migration 中出现过，但当前 `AppDbContext` 没有 `DbSet<FacilityBooking>` |

所以当前代码直接使用的 table 以 `AppDbContext` 的 16 个 DbSet 为准。

## 4. 主要 Table 关系

从 `AppDbContext.OnModelCreating(...)` 可以看到主要关系：

- `Property` 属于一个 landlord user：`Properties.LandlordId -> Users.Id`
- `Tenant` 属于一个 user：`Tenants.UserId -> Users.Id`
- `Tenant` 可关联一个 property：`Tenants.PropertyId -> Properties.PropertyId`
- `PropertyAmenity` 属于 property：`PropertyAmenities.PropertyId -> Properties.PropertyId`
- `MaintenanceRequest` 属于 tenant 和 property
- `MaintenanceTimeline` 属于 maintenance request
- `Payment` 属于 tenant 和 property
- `Document` 可关联 uploaded user、property、tenant
- `VisitorPass` 属于 tenant
- `PasswordResetRequest` 可关联 user
- `LeaseHistory` 属于 tenant
- `PropertyBooking` 属于 property，可选关联 promo code

多个 enum 字段使用 string 存进 PostgreSQL，例如 property type、availability status、payment status、maintenance status 等。

## 5. S3 使用情况

项目有明确使用 Amazon S3。

配置位置：

```json
"AWS": {
  "Region": "ap-southeast-1",
  "BucketName": "propease-community-images-2026"
}
```

使用的 bucket：

```text
propease-community-images-2026
```

主要 S3 用途：

| 用途 | 代码位置 | S3 key / folder |
| --- | --- | --- |
| 社区公告图片上传 | `CommunityAdminController` + `S3ImageService` | 默认 `community-hub/{guid}.{ext}` |
| landlord property 图片上传 | `LandlordController` + `S3ImageService` | `landlord-properties/{guid}.{ext}` |
| tenant document 上传 | `TenantController` | `tenant-documents/...` 相关 file key |
| landlord document 上传 | `LandlordController` | `landlord-documents/...` 相关 file key |
| email / QR image 上传 | `EmailService` | 上传后生成 pre-signed URL |
| Lambda payment receipt / event 相关文件 | `MyMvcApp.Serverless/StripeEventProcessor.cs` | 上传到 configured S3 bucket |

数据库里和 S3 相关的字段：

- `Documents.FileKey`
- `Documents.S3BucketName`
- `Documents.S3Url`
- `Payments.ReceiptFileKey`
- 多个 image URL 字段，例如 `Property.ImageUrl`、`CommunityUpdate.ImageUrl`

下载文件时，代码会优先用 S3 object key 生成 pre-signed URL；如果没有 S3 key，才 fallback 到 stored URL 或本地 file path。

## 6. Serverless / Lambda 使用情况

项目有一个独立 Lambda project：

```text
MyMvcApp.Serverless
```

用途是处理 Stripe EventBridge 付款事件。

相关流程在 `SERVERLESS_CHANGES.md` 中写成：

```text
Stripe -> Amazon EventBridge -> AWS Lambda -> PostgreSQL / S3 / SES
```

Lambda worker 使用的 PostgreSQL tables：

| Table | 用途 |
| --- | --- |
| `Payments` | 更新 Stripe payment / refund 状态 |
| `PropertyBookings` | 更新 booking payment 状态 |
| `Properties` | 查询 property 信息 |
| `AuditLogs` | 写入事件处理审计 |

Lambda 也会使用：

- PostgreSQL：通过 `DefaultConnection`
- S3：通过 `AWS:BucketName`
- SES：通过 `AWS:SesSenderEmail`

## 7. 其他 AWS 服务

除了 S3 / RDS 以外，项目还看到这些 AWS 服务：

| AWS 服务 | 用途 |
| --- | --- |
| Amazon Cognito | 用户认证，`AddCognitoIdentity()` 和 Cognito Identity Provider client |
| Amazon SES | 发送 email，配置 `AWS:SesSenderEmail` |
| AWS X-Ray | tracing，主 app 和 AWS SDK 调用都有 X-Ray 集成 |
| Amazon EventBridge | 接收 Stripe partner event，触发 Lambda worker |
| AWS Lambda | serverless Stripe event processor |
| Amazon S3 | 图片、文档、QR/receipt 等文件存储 |
| Amazon RDS PostgreSQL | 推测为生产 PostgreSQL 托管方式，需要从部署环境确认 endpoint |

## 8. 部署配置相关

`docker-compose.ec2.yml` 里主 app 是跑在 EC2/Docker 语境：

- app container: `mymvcapp`
- port: host `80` -> container `8080`
- X-Ray daemon sidecar: `amazon/aws-xray-daemon`
- AWS credentials 通过环境变量注入：
  - `AWS__AccessKey`
  - `AWS__SecretKey`
  - `AWS__UserPoolClientSecret`

注意：`docker-compose.ec2.yml` 里没有直接设置 `ConnectionStrings__DefaultConnection`，所以数据库连接字符串应该由其他部署方式、环境变量、secret manager 或服务器环境提供。

## 9. 总结

这个 project 主要使用：

- Database：PostgreSQL，通过 EF Core + Npgsql 访问。
- RDS：代码没有直接标明，但生产环境大概率是 Amazon RDS for PostgreSQL；需要确认实际 `DefaultConnection`。
- Tables：当前主 app 直接映射 16 个 EF Core tables。
- S3：bucket 是 `propease-community-images-2026`，用于图片、文档、QR/receipt 等文件。
- Serverless：`MyMvcApp.Serverless` 是 AWS Lambda worker，用 EventBridge 接 Stripe payment events，再更新 PostgreSQL、写 S3、发 SES email。
