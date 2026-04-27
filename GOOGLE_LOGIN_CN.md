# Google Login 配置文档

本文档说明如何在当前 ASP.NET Core MVC 项目 `MyMvcApp` 中配置和验证 Google Login。

当前项目已经包含 Google Login 功能，相关代码可以拆成以下几个部分。

## 1. Google Login 代码拆分

### 1.1 NuGet Package

Google 外部登录使用以下 NuGet package：

```text
Microsoft.AspNetCore.Authentication.Google
```

项目文件位置：

```text
MyMvcApp/MyMvcApp.csproj
```

### 1.2 配置入口

Google Login 的服务注册和 Client ID / Client Secret 读取已经拆到：

```text
MyMvcApp/Extensions/GoogleAuthenticationExtensions.cs
```

`Program.cs` 只保留一行调用：

```csharp
builder.Services.AddGoogleLogin(builder.Configuration);
```

主要负责：

- 从配置中读取 `Authentication:Google:ClientId`
- 从配置中读取 `Authentication:Google:ClientSecret`
- 调用 `AddGoogle(...)` 注册 Google authentication handler

### 1.3 登录页面入口

Google 登录表单已经拆到：

```text
MyMvcApp/Views/Account/_GoogleLoginForm.cshtml
```

登录页入口仍然位于：

```text
MyMvcApp/Views/Account/_AuthPage.cshtml
```

主要负责：

- 显示 `Continue with Google`
- 显示 `Sign up with Google`
- 提交到 `GoogleLoginController.ExternalLogin`
- 传入 `mode=login` 或 `mode=register`

### 1.4 Controller 处理

Google 登录跳转和回调处理位于：

```text
MyMvcApp/Controllers/GoogleLoginController.cs
```

主要方法：

- `ExternalLogin`
- `ExternalLoginCallback`

主要负责：

- 发起 Google OAuth challenge
- 接收 Google 回调结果
- 读取 Google 返回的 email
- 创建或匹配本地 `AppUser`
- 判断是否已审批、是否被禁用
- 根据用户角色跳转到对应 dashboard

## 2. 功能流程

用户点击登录页的 `Continue with Google` 或 `Sign up with Google` 后，系统会跳转到 Google OAuth 登录页面。

Google 验证成功后，会回调到本项目默认地址：

```text
/signin-google
```

之后项目会进入 `GoogleLoginController.ExternalLoginCallback`，并执行以下逻辑：

1. 读取 Google 返回的 email。
2. 使用 email 查找本地 `Users` 表中的 `AppUser`。
3. 如果用户不存在，自动创建一个 `Tenant` 角色用户，并设置 `IsApproved = false`，等待管理员审批。
4. 如果用户存在但被禁用，拒绝登录。
5. 如果用户存在但未审批，提示等待管理员审批。
6. 如果用户已审批，系统根据角色跳转：
   - `Admin` -> Admin Dashboard
   - `Landlord` -> Landlord Dashboard
   - 其他用户 -> Tenant Dashboard

## 3. Google Cloud Console 配置

### 3.1 创建 OAuth Client

1. 打开 Google Cloud Console。
2. 进入 `APIs & Services` -> `Credentials`。
3. 创建或选择一个项目。
4. 配置 `OAuth consent screen`。
5. 创建 `OAuth client ID`。
6. Application type 选择 `Web application`。
7. 保存生成的 `Client ID` 和 `Client Secret`。

### 3.2 配置 Authorized redirect URIs

ASP.NET Core Google provider 默认使用 `/signin-google` 作为回调路径，因此 Google Console 里必须加入完整回调 URL。

本地开发推荐添加：

```text
https://localhost:7118/signin-google
```

如果你使用 `http` profile 启动项目，也可以添加：

```text
http://localhost:5051/signin-google
```

生产环境应添加真实域名的 HTTPS 回调地址：

```text
https://your-domain.com/signin-google
```

注意：Google OAuth 对 redirect URI 匹配非常严格，协议、域名、端口、路径都必须完全一致。生产环境通常需要 HTTPS 和真实域名；直接使用 EC2 公网 IP 作为 Google OAuth 回调地址通常不适合。

## 4. 在项目中配置 Client ID 和 Secret

项目在 `GoogleAuthenticationExtensions.cs` 中读取以下配置：

```text
Authentication:Google:ClientId
Authentication:Google:ClientSecret
```

代码位置：

```csharp
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
```

只有当两个值都存在时，项目才会启用 Google Login。

### 4.1 本地开发配置方式

推荐使用 .NET User Secrets，不要把 Client Secret 提交到 Git。

在 `MyMvcApp` 目录运行：

```bash
dotnet user-secrets set "Authentication:Google:ClientId" "YOUR_GOOGLE_CLIENT_ID"
dotnet user-secrets set "Authentication:Google:ClientSecret" "YOUR_GOOGLE_CLIENT_SECRET"
```

然后启动项目：

```bash
dotnet run --launch-profile https
```

访问：

```text
https://localhost:7118/Account/Login
```

### 4.2 使用环境变量配置

Linux、Docker、EC2 环境建议使用双下划线 `__` 表示配置层级：

```bash
export Authentication__Google__ClientId="YOUR_GOOGLE_CLIENT_ID"
export Authentication__Google__ClientSecret="YOUR_GOOGLE_CLIENT_SECRET"
```

如果使用 Docker Compose，可以在 service 的 `environment` 中加入：

```yaml
environment:
  Authentication__Google__ClientId: "YOUR_GOOGLE_CLIENT_ID"
  Authentication__Google__ClientSecret: "YOUR_GOOGLE_CLIENT_SECRET"
```

不要把真实的 Client Secret 写进 `appsettings.json` 或提交到仓库。

## 5. 当前项目相关代码说明

### 5.1 GoogleAuthenticationExtensions.cs

项目通过扩展方法注册 Google 登录：

```csharp
builder.Services.AddGoogleLogin(builder.Configuration);
```

扩展方法内部会注册 Google authentication handler：

```csharp
builder.Services.AddAuthentication()
    .AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.SaveTokens = true;
    });
```

如果 `ClientId` 或 `ClientSecret` 为空，Google 登录不会注册。用户点击 Google 登录时会看到：

```text
Google login is not configured yet.
```

### 5.2 登录页面

登录页在 `_AuthPage.cshtml` 中引用 `_GoogleLoginForm.cshtml`，提供两个 Google 表单：

- 登录模式: `Continue with Google`
- 注册模式: `Sign up with Google`

两个表单都会提交到：

```text
POST /GoogleLogin/ExternalLogin
```

并传入：

```text
mode=login
mode=register
```

### 5.3 回调处理

Google 登录成功后，系统通过 `ExternalLoginCallback` 处理返回结果。

重点规则：

- Google 返回 email 后，本项目以 email 作为用户识别字段。
- 新 Google 用户默认创建为 `Tenant`。
- 新用户不会自动通过审批，需要 Admin 审批。
- 已审批用户会根据本地 `AppUser.Role` 跳转到对应 dashboard。

## 6. 测试步骤

1. 确认 Google Console 已添加正确 redirect URI。
2. 确认本地已配置 `Authentication:Google:ClientId` 和 `Authentication:Google:ClientSecret`。
3. 使用 HTTPS profile 启动：

```bash
dotnet run --launch-profile https
```

4. 打开：

```text
https://localhost:7118/Account/Login
```

5. 点击 `Continue with Google`。
6. 完成 Google 授权。
7. 如果是第一次 Google 登录，系统应提示注册成功并等待管理员审批。
8. 管理员审批用户后，再次点击 Google 登录，应进入对应 Dashboard。

## 7. 生产部署注意事项

生产环境建议：

- 使用正式域名，例如 `https://propease.example.com`。
- 配置 HTTPS，例如 Nginx、Caddy、AWS ALB 或 CloudFront。
- 在 Google Console 添加生产 redirect URI：

```text
https://propease.example.com/signin-google
```

- 在 EC2 或 Docker 环境中使用环境变量保存 Google Client ID 和 Secret。
- 不要把真实 secret 放进 `appsettings.json`、`appsettings.Development.json` 或 Git。

如果当前项目仍是直接通过：

```text
http://<EC2_PUBLIC_IP>
```

访问，那么建议先配置域名和 HTTPS，再启用生产 Google Login。

## 8. 常见问题排查

### 8.1 redirect_uri_mismatch

原因：Google Console 中的 Authorized redirect URI 和实际回调地址不完全一致。

检查：

- `http` 和 `https` 是否一致。
- 域名是否一致。
- 端口是否一致。
- 路径是否是 `/signin-google`。
- 结尾是否多了 `/`。

### 8.2 Google login is not configured yet

原因：项目没有读取到 Google Client ID 或 Client Secret。

检查：

```bash
dotnet user-secrets list
```

或检查部署环境变量：

```bash
printenv | grep Authentication__Google
```

### 8.3 Google 登录成功后仍然不能进入系统

当前项目有管理员审批机制。新 Google 用户会被创建为：

```text
Role = Tenant
IsApproved = false
```

需要 Admin 在用户管理页面审批后，用户才能进入系统。

### 8.4 Google 没有返回 email

通常是 OAuth consent screen 或 scope 配置问题。ASP.NET Core Google provider 默认会请求基础 profile 和 email 信息；如果仍然没有 email，需要检查 Google Cloud Console 的 OAuth 配置。

## 9. 参考资料

- Google OAuth 2.0 for Web Server Applications: https://developers.google.com/identity/protocols/oauth2/web-server
- ASP.NET Core Google external login setup: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/social/google-logins
