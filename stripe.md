# Stripe Payment Integration
# Stripe 租金支付流程说明

## 1. Current Implementation Status

本项目目前已经完成 Stripe payment 的第一阶段：

> Tenant 点击 Pay Rent  
> .NET 创建 Stripe Checkout Session  
> Redirect 到 Stripe 默认支付页面  
> Stripe 支付后回到 Success / Cancel 页面

当前实现重点是 **把租客从系统带到 Stripe Checkout，再从 Stripe 返回系统**。  
目前还没有实现 webhook、付款入库、receipt 更新和自动验证付款状态。

当前状态：**Stripe Checkout redirect flow 已完成**

---

## 2. Completed Flow

### Step 1: Tenant Opens Payment Page

Tenant 进入付款页面：

- `TenantController.Payments()`
- `Views/Tenant/Payments.cshtml`

页面会显示：

- Total Verified
- Payments Recorded
- Next Due
- Monthly Rent
- Payment History
- Pay Rent 按钮

相关文件：

- `MyMvcApp/Controllers/TenantController.cs`
- `MyMvcApp/Views/Tenant/Payments.cshtml`
- `MyMvcApp/Models/TenantPaymentsViewModel.cs`

---

### Step 2: Tenant Clicks Pay Rent

在 `Payments.cshtml` 中，租客点击 **Pay Rent** 后会打开 payment checkout panel。

Panel 显示当前系统计算出的付款信息：

- Payment Month
- Payment Year
- Amount Paid
- Payment Date

这些字段目前都是 readonly。  
真正提交时，表单会 POST 到：

```csharp
TenantController.CreateCheckoutSession()
```

Razor form：

```cshtml
<form asp-action="CreateCheckoutSession" method="post" id="paymentForm">
    @Html.AntiForgeryToken()
    ...
    <button type="submit">Continue to Stripe</button>
</form>
```

完成状态：**已完成**

---

### Step 3: .NET Creates Stripe Checkout Session

`TenantController.CreateCheckoutSession()` 会做以下事情：

1. 读取当前登录用户 email
2. 根据 email 查找当前 tenant
3. 如果 tenant 还没有 property assignment，则跳转到 `PendingAssignment`
4. 从 configuration 读取 `Stripe:SecretKey`
5. 设置 Stripe API key
6. 查询当前 tenant 的已有付款记录
7. 计算 next due date
8. 计算应付月份和年份
9. 读取 property name
10. 把 monthly rent 转换成 Stripe 使用的最小货币单位
11. 创建 Success URL
12. 创建 Cancel URL
13. 创建 Stripe Checkout Session
14. Redirect 到 `session.Url`

相关代码位置：

- `MyMvcApp/Controllers/TenantController.cs`

核心代码逻辑：

```csharp
var stripeSecretKey = _configuration["Stripe:SecretKey"];
Stripe.StripeConfiguration.ApiKey = stripeSecretKey;

var options = new SessionCreateOptions
{
    Mode = "payment",
    CustomerEmail = tenant.User.Email,
    SuccessUrl = successUrl,
    CancelUrl = cancelUrl,
    PaymentMethodTypes = new List<string> { "card" },
    LineItems = new List<SessionLineItemOptions>
    {
        new()
        {
            Quantity = 1,
            PriceData = new SessionLineItemPriceDataOptions
            {
                Currency = "myr",
                UnitAmount = unitAmount,
                ProductData = new SessionLineItemPriceDataProductDataOptions
                {
                    Name = $"Rent - {propertyName}",
                    Description = $"{monthName} {year} rent payment"
                }
            }
        }
    },
    Metadata = new Dictionary<string, string>
    {
        ["tenantId"] = tenant.TenantId.ToString(),
        ["propertyId"] = tenant.PropertyId.ToString(),
        ["paymentMonth"] = monthName,
        ["paymentYear"] = year.ToString()
    }
};

var service = new SessionService();
var session = await service.CreateAsync(options);

return Redirect(session.Url);
```

完成状态：**已完成**

---

### Step 4: Redirect To Stripe Checkout

创建 Checkout Session 成功后，系统会执行：

```csharp
return Redirect(session.Url);
```

这会把租客带到 Stripe 默认支付页面。

Stripe Checkout 页面由 Stripe 托管，因此本系统不需要自己处理：

- 银行卡 UI
- 卡号输入
- 支付安全校验
- Stripe payment form
- 3D Secure 页面

完成状态：**已完成**

---

### Step 5: Stripe Returns To Success Page

如果租客在 Stripe Checkout 完成付款，Stripe 会 redirect 回：

```csharp
TenantController.PaymentSuccess()
```

对应页面：

- `MyMvcApp/Views/Tenant/PaymentSuccess.cshtml`

当前 Success 页面只负责显示成功结果：

- Stripe Checkout
- Payment Successful
- Back to Payments 按钮

当前状态：**已完成**

注意：  
目前 Success 页面只是显示 Stripe 返回成功，并没有从 Stripe 查询 session，也没有更新本地 `Payment` record。

---

### Step 6: Stripe Returns To Cancel Page

如果租客取消付款，Stripe 会 redirect 回：

```csharp
TenantController.PaymentCancel()
```

对应页面：

- `MyMvcApp/Views/Tenant/PaymentCancel.cshtml`

当前 Cancel 页面只负责显示取消结果：

- Stripe Checkout
- Payment Cancelled
- Back to Payments 按钮

当前状态：**已完成**

注意：  
目前 Cancel 页面只是显示取消结果，并没有创建 failed payment record。

---

## 3. Files Changed

### 3.1 Controller

文件：

- `MyMvcApp/Controllers/TenantController.cs`

新增内容：

- 引入 `Stripe.Checkout`
- 注入 `IConfiguration`
- 新增 `CreateCheckoutSession()`
- 新增 `PaymentSuccess()`
- 新增 `PaymentCancel()`

主要用途：

- 创建 Stripe Checkout Session
- Redirect 到 Stripe hosted checkout page
- 接收 Stripe success / cancel redirect

---

### 3.2 Tenant Payment View

文件：

- `MyMvcApp/Views/Tenant/Payments.cshtml`

修改内容：

- `Pay Now` 按钮改成 `Pay Rent`
- form action 改成 `CreateCheckoutSession`
- submit 按钮改成 `Continue to Stripe`
- loading modal 改成 `Opening Stripe`
- 新增显示 `TempData["ErrorMessage"]`

主要用途：

- 让租客从 payment page 发起 Stripe checkout

---

### 3.3 Success Page

文件：

- `MyMvcApp/Views/Tenant/PaymentSuccess.cshtml`

用途：

- Stripe 支付成功后返回此页面
- 显示 Payment Successful
- 提供 Back to Payments 按钮

---

### 3.4 Cancel Page

文件：

- `MyMvcApp/Views/Tenant/PaymentCancel.cshtml`

用途：

- Stripe 支付取消后返回此页面
- 显示 Payment Cancelled
- 提供 Back to Payments 按钮

---

## 4. Configuration

Stripe key 配置在：

- `MyMvcApp/appsettings.json`

配置结构：

```json
"Stripe": {
  "SecretKey": "REPLACE_WITH_STRIPE_SECRET_KEY",
  "PublishableKey": "REPLACE_WITH_STRIPE_PUBLISHABLE_KEY"
}
```

当前代码只使用：

```csharp
_configuration["Stripe:SecretKey"]
```

`PublishableKey` 当前还没有用到，因为本项目使用的是 Stripe hosted Checkout 页面，不是自建前端 card element。

安全建议：

- 不要把真实 secret key commit 到 GitHub
- 本地开发可以用 User Secrets
- 部署环境建议使用环境变量或 AWS Secrets Manager
- `SecretKey` 只能放在 server side
- `PublishableKey` 才可以给前端使用

---

## 5. Current Payment Data Behavior

目前这个阶段 **不会创建新的 Payment record**。

也就是说：

- 点击 Pay Rent 不会马上写入 `Payments` 表
- Stripe checkout success 后也不会更新 `Payments` 表
- Stripe checkout cancel 后也不会更新 `Payments` 表
- Payment History 仍然显示原本数据库里的 payment records

原因：  
当前目标只做到 Stripe redirect flow，不包含 webhook 和本地付款状态同步。

当前行为是刻意保留的，避免在没有 webhook 验证前错误地把付款标记成 `Verified`。

---

## 6. Current Flow Diagram

```text
Tenant Payments Page
        |
        v
Click Pay Rent
        |
        v
POST /Tenant/CreateCheckoutSession
        |
        v
.NET calculates next rent month and amount
        |
        v
.NET creates Stripe Checkout Session
        |
        v
Redirect to Stripe hosted checkout page
        |
        +--------------------------+
        |                          |
        v                          v
Stripe success                Stripe cancel
        |                          |
        v                          v
/Tenant/PaymentSuccess        /Tenant/PaymentCancel
```

---

## 7. What Is Completed

| Feature | Status |
| --- | --- |
| Stripe.net package | Completed |
| Stripe secret key config reading | Completed |
| Tenant Pay Rent button | Completed |
| Create Checkout Session | Completed |
| Redirect to Stripe hosted page | Completed |
| Success return page | Completed |
| Cancel return page | Completed |
| Webhook payment confirmation | Not yet |
| Save Stripe session id | Not yet |
| Save Stripe payment intent id | Not yet |
| Update local Payment status | Not yet |
| Generate receipt after Stripe success | Not yet |

---

## 8. Important Limitations

### 8.1 Success Page Is Not Proof Of Payment

目前 `PaymentSuccess` 只是 Stripe redirect 后的页面。  
正式企业系统不能只靠 success redirect 来确认付款。

原因：

- 用户可能刷新或复制 success URL
- redirect 不是可靠的后台确认机制
- Stripe 官方推荐使用 webhook 来确认付款完成

正式付款确认必须通过：

```text
Stripe webhook: checkout.session.completed
```

---

### 8.2 No Payment Record Is Created Yet

当前没有创建 `Payment` record。  
这是因为系统还没有 webhook，不能可靠确认付款结果。

下一阶段可以选择：

1. 创建 Checkout Session 前先创建 `Pending` payment
2. 保存 `StripeSessionId`
3. webhook 收到成功后更新为 `Verified`
4. webhook 收到失败或过期后更新为 `Rejected` 或 `Pending`

---

### 8.3 No Receipt Update Yet

当前 Success 页面不会生成 receipt。  
下一阶段建议：

- 从 Stripe session / payment intent 读取 receipt URL
- 保存到 `Payment.ReceiptFileKey` 或新增 `StripeReceiptUrl`
- Payment History 显示 Stripe receipt link

---

## 9. Recommended Next Step

下一阶段建议实现：

### Phase 2: Webhook And Payment Persistence

1. 给 `Payment` model 新增 Stripe 字段：
   - `StripeSessionId`
   - `StripePaymentIntentId`
   - `StripeReceiptUrl`
2. 创建 migration
3. 创建 Checkout Session 前先创建 `Pending` payment
4. 把 `paymentId` 放进 Stripe metadata
5. 设置 Success URL 带 `session_id`
6. 新增 Stripe webhook endpoint
7. 监听 `checkout.session.completed`
8. webhook 成功后更新 Payment 为 `Verified`
9. 保存 Stripe payment intent id
10. 保存 Stripe receipt URL
11. Payment History 显示 Stripe receipt

---

## 10. Final Judgment

当前 Stripe payment 已经完成了最关键的第一步：

> Tenant 可以从系统进入 Stripe 默认支付页面，并根据支付结果回到 Success 或 Cancel 页面。

这说明系统已经具备真实 payment gateway 的入口，不再只是纯 mock payment。

不过，目前它还不是完整企业级付款闭环。  
要变成完整 A+ payment flow，还需要补：

- Payment record persistence
- Stripe webhook verification
- Payment status sync
- Stripe receipt URL
- Admin / Landlord payment monitoring
- Payment audit log

当前完成度：**Stripe Checkout Redirect Flow Completed**
