# Stripe Payment Integration Guide
# Stripe 支付接入完整指南

## 项目现状

- Stripe.net 包 **已安装** (v51.1.0)
- `appsettings.json` 有 Stripe 配置但还是占位符 (`sk_test_xxx`)
- 当前是 mock payment，直接 `Status = Verified`
- 需要改成真实 Stripe Checkout 流程

---

## Step 1：获取 Stripe API Keys

1. 登录 [stripe.com](https://stripe.com) → 注册账号
2. 进入 Dashboard → **Developers → API keys**
3. 复制两个 key：
   - `Publishable key`（`pk_test_...`）
   - `Secret key`（`sk_test_...`）

更新 `appsettings.json`：

```json
"Stripe": {
  "SecretKey": "sk_test_你的真实key",
  "PublishableKey": "pk_test_你的真实key",
  "WebhookSecret": "whsec_你的webhook密钥"
}
```

---

## Step 2：给 Payment 模型添加 Stripe 字段

编辑 `Models/Payment.cs`，在 `LandlordRemarks` 后面添加三个字段：

```csharp
[MaxLength(200)]
public string? StripeSessionId { get; set; }       // Checkout Session ID

[MaxLength(200)]
public string? StripePaymentIntentId { get; set; } // PaymentIntent ID

[MaxLength(500)]
public string? StripeReceiptUrl { get; set; }      // Stripe 收据 URL
```

然后跑 Migration：

```bash
cd /Users/project/dotnet/SuperNiubi_AWS_PROJECT
dotnet ef migrations add AddStripeFieldsToPayment --project MyMvcApp
dotnet ef database update --project MyMvcApp
```

---

## Step 3：注册 Stripe 到 Program.cs

在 `Program.cs` 顶部 using 区加：

```csharp
using Stripe;
```

在 `var app = builder.Build();` 前面加：

```csharp
StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];
```

---

## Step 4：创建 StripeController

新建文件 `Controllers/StripeController.cs`：

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Models;
using Stripe;
using Stripe.Checkout;
using System.Security.Claims;

namespace MyMvcApp.Controllers
{
    public class StripeController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;

        public StripeController(ApplicationDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        // Step A: Tenant 点击 Pay Now → 创建 Checkout Session → 跳转 Stripe
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCheckoutSession()
        {
            var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(email))
                return RedirectToAction("Login", "Account");

            var tenant = await _context.Tenants
                .Include(t => t.User)
                .Include(t => t.Property)
                .FirstOrDefaultAsync(t => t.User.Email == email && !t.User.IsDisabled);

            if (tenant == null)
                return RedirectToAction("PendingAssignment", "Tenant");

            // 计算应付月份
            var payments = await _context.Payments
                .Where(p => p.TenantId == tenant.TenantId)
                .ToListAsync();

            var now = DateTime.UtcNow;
            var nextDue = GetNextDueDate(tenant.RentDueDay, payments, now);
            var monthName = System.Globalization.CultureInfo.InvariantCulture
                .DateTimeFormat.GetMonthName(nextDue.Month);

            // 创建 Pending 付款记录
            var dueDay = Math.Clamp(tenant.RentDueDay, 1,
                DateTime.DaysInMonth(nextDue.Year, nextDue.Month));
            var dueDate = new DateTime(nextDue.Year, nextDue.Month, dueDay,
                0, 0, 0, DateTimeKind.Utc);

            var payment = new Payment
            {
                TenantId = tenant.TenantId,
                PropertyId = tenant.PropertyId,
                PaymentMonth = monthName,
                PaymentYear = nextDue.Year,
                Amount = tenant.MonthlyRent,
                DueDate = dueDate,
                PaymentMethod = PaymentMethod.OnlineTransfer,
                Status = PaymentStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();

            // 创建 Stripe Checkout Session
            var domain = $"{Request.Scheme}://{Request.Host}";
            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = "myr",
                            UnitAmount = (long)(tenant.MonthlyRent * 100), // 分为单位
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = $"Rent - {monthName} {nextDue.Year}",
                                Description = $"{tenant.Property?.PropertyName} | {tenant.Property?.Address}"
                            }
                        },
                        Quantity = 1
                    }
                },
                Mode = "payment",
                CustomerEmail = email,
                SuccessUrl = $"{domain}/Stripe/PaymentSuccess?session_id={{CHECKOUT_SESSION_ID}}&paymentId={payment.PaymentId}",
                CancelUrl = $"{domain}/Stripe/PaymentCancelled?paymentId={payment.PaymentId}",
                Metadata = new Dictionary<string, string>
                {
                    { "paymentId", payment.PaymentId.ToString() },
                    { "tenantId", tenant.TenantId.ToString() }
                }
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            // 保存 Session ID
            payment.StripeSessionId = session.Id;
            payment.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Redirect(session.Url);
        }

        // Step B: 付款成功后 Stripe 跳回这里
        public async Task<IActionResult> PaymentSuccess(string session_id, int paymentId)
        {
            var service = new SessionService();
            var session = await service.GetAsync(session_id);

            if (session.PaymentStatus == "paid")
            {
                var payment = await _context.Payments.FindAsync(paymentId);
                if (payment != null && payment.Status != PaymentStatus.Verified)
                {
                    payment.Status = PaymentStatus.Verified;
                    payment.PaymentDate = DateTime.UtcNow;
                    payment.StripeSessionId = session.Id;
                    payment.StripePaymentIntentId = session.PaymentIntentId;
                    payment.ReferenceNo = session.PaymentIntentId;
                    payment.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
            }

            TempData["SuccessMessage"] = "Payment successful! Your receipt has been recorded.";
            return RedirectToAction("Payments", "Tenant");
        }

        // Step C: 用户取消付款后跳回这里
        public async Task<IActionResult> PaymentCancelled(int paymentId)
        {
            var payment = await _context.Payments.FindAsync(paymentId);
            if (payment != null && payment.Status == PaymentStatus.Pending)
            {
                payment.Status = PaymentStatus.Rejected;
                payment.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            TempData["ErrorMessage"] = "Payment was cancelled.";
            return RedirectToAction("Payments", "Tenant");
        }

        // Step D: Stripe Webhook（正式环境必须用这个来更新状态）
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Webhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            var webhookSecret = _config["Stripe:WebhookSecret"];

            Stripe.Event stripeEvent;
            try
            {
                stripeEvent = EventUtility.ConstructEvent(
                    json,
                    Request.Headers["Stripe-Signature"],
                    webhookSecret);
            }
            catch (StripeException)
            {
                return BadRequest();
            }

            if (stripeEvent.Type == Events.CheckoutSessionCompleted)
            {
                var session = stripeEvent.Data.Object as Session;
                if (session?.Metadata != null &&
                    session.Metadata.TryGetValue("paymentId", out var pidStr) &&
                    int.TryParse(pidStr, out var paymentId))
                {
                    var payment = await _context.Payments.FindAsync(paymentId);
                    if (payment != null && payment.Status != PaymentStatus.Verified)
                    {
                        payment.Status = PaymentStatus.Verified;
                        payment.PaymentDate = DateTime.UtcNow;
                        payment.StripeSessionId = session.Id;
                        payment.StripePaymentIntentId = session.PaymentIntentId;
                        payment.ReferenceNo = session.PaymentIntentId;
                        payment.UpdatedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync();
                    }
                }
            }

            return Ok();
        }

        private static DateTime GetNextDueDate(int rentDueDay, IEnumerable<Payment> payments, DateTime now)
        {
            var candidate = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < 24; i++)
            {
                var alreadyPaid = payments.Any(p =>
                    p.Status == PaymentStatus.Verified &&
                    p.PaymentYear == candidate.Year &&
                    p.PaymentMonth == System.Globalization.CultureInfo.InvariantCulture
                        .DateTimeFormat.GetMonthName(candidate.Month));
                if (!alreadyPaid) return candidate;
                candidate = candidate.AddMonths(1);
            }
            return candidate;
        }
    }
}
```

---

## Step 5：更新 Payments.cshtml（把 Pay Now 改成 Stripe）

找到 `Views/Tenant/Payments.cshtml` 中原来的 `<form asp-action="UploadPayment">` 整个表单，替换成：

```html
<form asp-controller="Stripe" asp-action="CreateCheckoutSession" method="post">
    @Html.AntiForgeryToken()
    <div class="tenant-panel-body" style="padding: 20px;">
        <p>You will be redirected to Stripe to complete your payment securely.</p>
        <ul>
            <li><strong>Month:</strong> @displayMonthName @Model.NewPayment.PaymentYear</li>
            <li><strong>Amount:</strong> RM @Model.NewPayment.Amount.ToString("F2")</li>
        </ul>
        <button type="submit" class="btn tenant-btn tenant-btn-primary">
            <i class="bi bi-credit-card"></i> Pay with Stripe
        </button>
    </div>
</form>
```

同时在支付记录列表中，可以显示 Stripe Receipt 链接（如果有）：

```html
@if (!string.IsNullOrEmpty(payment.StripeReceiptUrl))
{
    <a href="@payment.StripeReceiptUrl" target="_blank" class="btn btn-sm btn-outline-secondary">
        <i class="bi bi-receipt"></i> Stripe Receipt
    </a>
}
```

---

## Step 6：Webhook 本地测试（Stripe CLI）

**安装 Stripe CLI（macOS）：**

```bash
brew install stripe/stripe-cli/stripe
stripe login
```

**启动本地 webhook 转发：**

```bash
stripe listen --forward-to https://localhost:5001/Stripe/Webhook
```

CLI 会输出一个 `whsec_...` 密钥，填入 `appsettings.Development.json`：

```json
"Stripe": {
  "WebhookSecret": "whsec_从CLI复制的密钥"
}
```

**测试触发付款成功事件：**

```bash
stripe trigger checkout.session.completed
```

---

## Step 7：Program.cs Webhook 路由（防止 Anti-Forgery 拦截）

在 `Program.cs` 路由配置中，确保 Webhook 路径被正确映射且不走 CSRF 检查：

```csharp
// Stripe Webhook 不需要 ANTIFORGERY，直接 map
app.MapControllerRoute(
    name: "stripe-webhook",
    pattern: "Stripe/Webhook",
    defaults: new { controller = "Stripe", action = "Webhook" });

// 默认路由放在后面
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
```

---

## 完整支付流程图

```
Tenant 点击 Pay Now
       ↓
POST /Stripe/CreateCheckoutSession
       ↓
创建 Payment (Status=Pending) + 保存 StripeSessionId
       ↓
跳转到 Stripe 托管支付页面
       ↓
    ┌──────────────────────────────┐
    │   用户填写信用卡 / FPX 信息   │
    └──────────────────────────────┘
         ↓                     ↓
      付款成功              用户取消
         ↓                     ↓
GET /Stripe/PaymentSuccess   GET /Stripe/PaymentCancelled
         ↓                     ↓
  Status = Verified       Status = Rejected
         ↓
同时 Stripe 后台 POST /Stripe/Webhook
（最可靠的状态更新方式，不依赖浏览器跳转）
```

---

## 测试用信用卡号（Stripe Test Mode）

| 卡号 | 结果 |
|------|------|
| `4242 4242 4242 4242` | 付款成功 |
| `4000 0000 0000 0002` | 卡被拒绝 |
| `4000 0025 0000 3155` | 需要 3D Secure 验证 |

到期日随便填未来日期，CVV 随便填 3 位数字。

---

## 优先级总结

| 步骤 | 是否必须 | 说明 |
|------|----------|------|
| Step 1 获取真实 key | ✅ 必须 | 替换 `sk_test_xxx` |
| Step 2 Payment 加字段 + Migration | ✅ 必须 | 存 SessionId / PaymentIntentId |
| Step 3 注册 Stripe 到 Program.cs | ✅ 必须 | 初始化 API Key |
| Step 4 创建 StripeController | ✅ 必须 | 核心业务逻辑 |
| Step 5 更新 Payments.cshtml | ✅ 必须 | 替换 Pay Now 按钮 |
| Step 6 Webhook 本地测试 | 推荐 | 正式上线必须配置 |
| Step 7 Program.cs 路由配置 | 推荐 | 防止 webhook 被拦截 |

---

## 部署到 EC2 后的 Webhook 配置

本地开发用 Stripe CLI 转发，部署到 EC2 后需要在 Stripe Dashboard 配置正式 Webhook：

1. Stripe Dashboard → **Developers → Webhooks → Add endpoint**
2. Endpoint URL：`https://你的域名/Stripe/Webhook`
3. 监听事件选择：`checkout.session.completed`
4. 保存后复制 **Signing secret**（`whsec_...`）
5. 填入服务器的环境变量或 `appsettings.json`

```bash
# EC2 上设置环境变量（推荐，不要把真实 key 写进代码）
export Stripe__SecretKey="sk_live_..."
export Stripe__WebhookSecret="whsec_..."
```
