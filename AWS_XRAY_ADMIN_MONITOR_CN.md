# AWS X-Ray Admin Monitor Setup

这份文档说明如何在当前 `MyMvcApp` 项目里接入 AWS X-Ray，并在 Admin sidebar 增加一个 `X-Ray Monitor` 功能，用来监控网站 request、慢请求、error、fault，帮助定位 production 问题。

> 注意：AWS 已宣布 X-Ray SDK 和 X-Ray daemon 进入维护模式。新项目更推荐 OpenTelemetry + AWS Distro for OpenTelemetry。不过如果课程或项目要求明确写 AWS X-Ray，下面这个方案可以直接用于当前 ASP.NET Core MVC 项目。

## 1. 当前已完成的部分

你现在已经完成了这些文件改动：

### `MyMvcApp/MyMvcApp.csproj`

已加入 X-Ray 相关 NuGet package：

```xml
<PackageReference Include="AWSXRayRecorder.Handlers.AspNetCore" Version="2.11.0" />
<PackageReference Include="AWSXRayRecorder.Handlers.AwsSdk" Version="2.11.0" />
<PackageReference Include="AWSSDK.XRay" Version="4.0.0" />
```

### `MyMvcApp/Program.cs`

已加入：

```csharp
using Amazon.XRay.Recorder.Handlers.AwsSdk;
```

并且已经注册 AWS SDK tracing：

```csharp
AWSSDKHandler.RegisterXRayForAllServices();
```

也已经启用 request tracing：

```csharp
app.UseXRay("PropEase");
```

### `MyMvcApp/appsettings.json`

已加入：

```json
"XRay": {
  "AWSXRayPlugins": "EC2Plugin",
  "SamplingRuleManifest": "sampling-rules.json"
}
```

## 2. 还需要补的文件：sampling rules

因为 `appsettings.json` 指定了 `sampling-rules.json`，所以建议在 `MyMvcApp` folder 下建立：

```text
MyMvcApp/sampling-rules.json
```

内容可以先用这个：

```json
{
  "version": 2,
  "rules": [
    {
      "description": "Trace admin and production web requests",
      "service_name": "PropEase",
      "http_method": "*",
      "url_path": "*",
      "fixed_target": 1,
      "rate": 0.5
    }
  ],
  "default": {
    "fixed_target": 1,
    "rate": 0.1
  }
}
```

解释：

- `fixed_target: 1` 表示每秒至少保留 1 个 request trace。
- `rate: 0.5` 表示额外 50% request 会被采样。
- Production 流量大时可以把 `rate` 降到 `0.05` 或 `0.1`。

## 3. Docker EC2 部署要加 X-Ray daemon

X-Ray SDK 不会自己直接把 trace 写进 AWS。它会先把 trace 发给 X-Ray daemon，然后 daemon 上传到 AWS X-Ray。

在 `docker-compose.ec2.yml` 里加一个 service：

```yaml
services:
  xray-daemon:
    image: public.ecr.aws/xray/aws-xray-daemon:3.x
    container_name: xray-daemon
    command: ["-o", "-n", "ap-southeast-1"]
    ports:
      - "2000:2000/udp"
      - "2000:2000/tcp"
    restart: unless-stopped

  mymvcapp:
    environment:
      AWS_XRAY_DAEMON_ADDRESS: xray-daemon:2000
      AWS_REGION: ap-southeast-1
```

如果 `mymvcapp` 已经有 `environment`，不要重复写第二个 `environment`，直接把这两行加进去：

```yaml
AWS_XRAY_DAEMON_ADDRESS: xray-daemon:2000
AWS_REGION: ap-southeast-1
```

## 4. EC2 IAM 权限

推荐给 EC2 instance attach IAM Role，不要把 AWS access key 写死在项目里。

Daemon 上传 traces 需要：

```text
AWSXRayDaemonWriteAccess
```

如果 Admin 页面要读取 X-Ray trace summary，还需要以下权限：

```text
xray:GetTraceSummaries
xray:BatchGetTraces
xray:GetServiceGraph
```

## 5. Admin sidebar 增加 X-Ray Monitor

### 5.1 修改 `_AdminRail.cshtml`

在 `MyMvcApp/Views/Admin/_AdminRail.cshtml` 的 sidebar nav 里加：

```html
<button type="button"
        class="landlord-rail-link admin-rail-switch"
        data-admin-switch="xray"
        data-admin-kicker="Diagnostics"
        data-admin-title="X-Ray Monitor"
        aria-pressed="false">
    <i class="bi bi-activity"></i>
    X-Ray Monitor
</button>
```

### 5.2 修改 `AdminController.cs`

找到：

```csharp
private static readonly string[] AllowedAdminPanes = { ... };
```

把 `xray` 加进去：

```csharp
private static readonly string[] AllowedAdminPanes =
{
    "dashboard",
    "users",
    "properties",
    "maintenance",
    "payments",
    "audit",
    "announcements",
    "xray"
};
```

### 5.3 注册 X-Ray client

在 `Program.cs` 里加：

```csharp
builder.Services.AddAWSService<Amazon.XRay.IAmazonXRay>();
```

建议放在现有 AWS service 附近，例如：

```csharp
builder.Services.AddAWSService<Amazon.S3.IAmazonS3>();
builder.Services.AddAWSService<Amazon.XRay.IAmazonXRay>();
```

### 5.4 建立 ViewModel

可以在 `Models/Admin/AdminDashboardViewModel.cs` 加：

```csharp
public class AdminXRayReportViewModel
{
    public int TotalTraces { get; set; }
    public int ErrorCount { get; set; }
    public int FaultCount { get; set; }
    public int ThrottleCount { get; set; }
    public double SlowestDuration { get; set; }
    public List<AdminXRayTraceItemViewModel> RecentTraces { get; set; } = new();
}

public class AdminXRayTraceItemViewModel
{
    public string TraceId { get; set; } = string.Empty;
    public double Duration { get; set; }
    public bool HasError { get; set; }
    public bool HasFault { get; set; }
    public bool HasThrottle { get; set; }
    public DateTime? StartTime { get; set; }
}
```

然后在 `AdminDashboardViewModel` 里加：

```csharp
public AdminXRayReportViewModel XRayReport { get; set; } = new();
```

### 5.5 Controller 查询最近 traces

在 `AdminController` constructor 注入：

```csharp
private readonly Amazon.XRay.IAmazonXRay _xray;
```

然后 constructor 参数加：

```csharp
Amazon.XRay.IAmazonXRay xray
```

赋值：

```csharp
_xray = xray;
```

在 `Dashboard` action 里建立 report：

```csharp
var traceResponse = await _xray.GetTraceSummariesAsync(new Amazon.XRay.Model.GetTraceSummariesRequest
{
    StartTime = DateTime.UtcNow.AddMinutes(-15),
    EndTime = DateTime.UtcNow
});

var traceSummaries = traceResponse.TraceSummaries ?? new List<Amazon.XRay.Model.TraceSummary>();

var xrayReport = new AdminXRayReportViewModel
{
    TotalTraces = traceSummaries.Count,
    ErrorCount = traceSummaries.Count(t => t.HasError == true),
    FaultCount = traceSummaries.Count(t => t.HasFault == true),
    ThrottleCount = traceSummaries.Count(t => t.HasThrottle == true),
    SlowestDuration = traceSummaries.Count == 0 ? 0 : traceSummaries.Max(t => t.Duration.GetValueOrDefault()),
    RecentTraces = traceSummaries
        .OrderByDescending(t => t.StartTime)
        .Take(10)
        .Select(t => new AdminXRayTraceItemViewModel
        {
            TraceId = t.Id,
            Duration = t.Duration.GetValueOrDefault(),
            HasError = t.HasError.GetValueOrDefault(),
            HasFault = t.HasFault.GetValueOrDefault(),
            HasThrottle = t.HasThrottle.GetValueOrDefault(),
            StartTime = t.StartTime
        })
        .ToList()
};
```

然后放进 `AdminDashboardViewModel`：

```csharp
XRayReport = xrayReport,
```

## 6. 建立 `_AdminXRayPane.cshtml`

新增：

```text
MyMvcApp/Views/Admin/_AdminXRayPane.cshtml
```

内容：

```cshtml
@model AdminDashboardViewModel

<section class="admin-pane d-none" data-admin-pane="xray">
    <div class="panel-card">
        <div class="panel-heading">
            <div>
                <h2 class="panel-title">X-Ray Monitor</h2>
                <span class="panel-meta">Recent traces from the last 15 minutes</span>
            </div>
            <span class="badge admin-count-badge">@Model.XRayReport.TotalTraces trace(s)</span>
        </div>

        <div class="row g-3 mb-3">
            <div class="col-md-3">
                <div class="summary-card">
                    <span>Total traces</span>
                    <strong>@Model.XRayReport.TotalTraces</strong>
                </div>
            </div>
            <div class="col-md-3">
                <div class="summary-card">
                    <span>Errors</span>
                    <strong>@Model.XRayReport.ErrorCount</strong>
                </div>
            </div>
            <div class="col-md-3">
                <div class="summary-card">
                    <span>Faults</span>
                    <strong>@Model.XRayReport.FaultCount</strong>
                </div>
            </div>
            <div class="col-md-3">
                <div class="summary-card">
                    <span>Slowest</span>
                    <strong>@Model.XRayReport.SlowestDuration.ToString("0.00")s</strong>
                </div>
            </div>
        </div>

        @if (Model.XRayReport.RecentTraces.Count == 0)
        {
            <div class="empty-state">No X-Ray traces found in the last 15 minutes.</div>
        }
        else
        {
            <div class="table-responsive">
                <table class="table admin-table">
                    <thead>
                        <tr>
                            <th>Trace ID</th>
                            <th>Duration</th>
                            <th>Status</th>
                            <th>Start time</th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var trace in Model.XRayReport.RecentTraces)
                        {
                            <tr>
                                <td>@trace.TraceId</td>
                                <td>@trace.Duration.ToString("0.00")s</td>
                                <td>
                                    @if (trace.HasFault)
                                    {
                                        <span class="badge badge-soft-danger">Fault</span>
                                    }
                                    else if (trace.HasError)
                                    {
                                        <span class="badge badge-soft-warning">Error</span>
                                    }
                                    else if (trace.HasThrottle)
                                    {
                                        <span class="badge badge-soft-info">Throttle</span>
                                    }
                                    else
                                    {
                                        <span class="badge badge-soft-success">OK</span>
                                    }
                                </td>
                                <td>@(trace.StartTime.HasValue ? trace.StartTime.Value.ToLocalTime().ToString("dd MMM yyyy, HH:mm") : "-")</td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
        }
    </div>
</section>
```

## 7. 在 Admin.cshtml 引入 pane

打开：

```text
MyMvcApp/Views/Admin/Admin.cshtml
```

在其他 partial 附近加：

```cshtml
<partial name="_AdminXRayPane" model="Model" />
```

## 8. 测试方式

本地先确认 build：

```bash
dotnet build MyMvcApp/MyMvcApp.csproj
```

EC2 部署后：

```bash
docker compose -f docker-compose.ec2.yml up -d --build
docker logs mymvcapp
docker logs xray-daemon
```

然后访问网站几次，再去：

```text
AWS Console -> X-Ray -> Traces
AWS Console -> X-Ray -> Service map
```

Admin 页面里进入：

```text
Admin Console -> X-Ray Monitor
```

应该可以看到最近 15 分钟 traces。

## 9. 常见问题

### Admin 页面没有 traces

检查：

- `xray-daemon` container 是否 running。
- `AWS_XRAY_DAEMON_ADDRESS` 是否是 `xray-daemon:2000`。
- EC2 IAM Role 是否有 `AWSXRayDaemonWriteAccess`。
- 网站有没有真的收到 request。
- AWS region 是否是 `ap-southeast-1`。

### X-Ray console 有 traces，但 Admin 页面没有

检查：

- App IAM Role 是否有 `xray:GetTraceSummaries`。
- `Program.cs` 是否注册了 `builder.Services.AddAWSService<Amazon.XRay.IAmazonXRay>();`。
- `AllowedAdminPanes` 是否加了 `xray`。

### Build 可以过，但 runtime 报 sampling-rules.json

检查：

- `MyMvcApp/sampling-rules.json` 是否存在。
- Docker image 是否把它复制进去了。
- `Dockerfile` 是否使用 `dotnet publish`，正常 publish 会包含项目内容；如果没有，设置 file copy behavior。

## 10. 官方参考

- AWS X-Ray SDK for .NET: https://docs.aws.amazon.com/xray/latest/devguide/xray-sdk-dotnet.html
- ASP.NET Core middleware: https://docs.aws.amazon.com/xray/latest/devguide/xray-sdk-dotnet-messagehandler.html
- X-Ray daemon: https://docs.aws.amazon.com/xray/latest/devguide/xray-daemon.html
- X-Ray SDK and daemon maintenance notice: https://docs.aws.amazon.com/xray/latest/devguide/xray-sdk-dotnet.html
