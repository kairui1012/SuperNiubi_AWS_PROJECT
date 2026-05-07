# ASP.NET Core Identity Cookie 时好时坏问题 - 修复方案

## 问题诊断
- 云端登录有时正常，有时被重定向回 Login 页面
- CheckAuth endpoint 显示：同一 cookie 有时被识别（isAuthenticated=true），有时不被识别（isAuthenticated=false）
- 根本原因：多个 container/instance 的 DataProtection keys 不同步，导致 cookie 解密失败

## 实施的修复

### 1. Program.cs 修改

已加入以下配置：

#### a) DataProtection Key 持久化
```csharp
var dataProtectionKeysPath = Environment.GetEnvironmentVariable("DATAPROTECTION_KEYS_PATH") 
    ?? (builder.Environment.IsDevelopment() 
        ? Path.Combine(builder.Environment.ContentRootPath, "DataProtection-Keys")
        : "/var/propease/dataprotection-keys");

Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("PropEase");
```
- **关键点**：从环境变量读取路径，支持灵活部署
- **本地开发**：使用相对路径 `DataProtection-Keys/`
- **云端生产**：使用 `/var/propease/dataprotection-keys`（必须是共享存储！）

#### b) Forwarded Headers 配置
```csharp
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
```
- 允许 Nginx/ALB 通过 `X-Forwarded-Proto` 告知原始请求是否为 HTTPS
- `Clear()` 的目的是信任来自 Nginx 的所有转发头

#### c) 中间件顺序（关键！）
```csharp
app.UseForwardedHeaders();  // 必须首先处理代理头
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
```
- `UseForwardedHeaders()` **必须** 在 `UseHttpsRedirection()` 和 `UseAuthentication()` 之前

#### d) Identity Application Cookie 配置
```csharp
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;  // 仅通过 HTTPS 发送
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Path = "/";
    options.Cookie.Name = ".AspNetCore.Identity.Application";
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
});
```
- `SecurePolicy = Always`：确保 cookie 仅通过 HTTPS 发送

#### e) Debug Middleware
```csharp
app.Use(async (context, next) =>
{
    // 记录 Auth 状态、cookie 状态、机器/PID 等
    // 只记录 /Admin, /Account/CheckAuth, /Account/Login 路径，减少日志噪音
    Console.WriteLine(
        $"[AUTH_DEBUG] Machine={machine} | PID={processId} | Host={host} | Scheme={scheme} | " +
        $"IsAuth={isAuth} | HasIdentityCookie={hasIdentityCookie}"
    );
    await next();
});
```
- 帮助诊断认证失败的原因

### 2. Dockerfile 修改

```dockerfile
# 创建持久化目录，用于 mount volume
RUN mkdir -p /var/propease/dataprotection-keys && chmod 755 /var/propease/dataprotection-keys
```

### 3. docker-compose.ec2.yml 修改

```yaml
services:
  mymvcapp:
    volumes:
      # CRITICAL: 挂载持久化卷以存储 DataProtection keys
      - /var/propease/dataprotection-keys:/var/propease/dataprotection-keys
    
    environment:
      DATAPROTECTION_KEYS_PATH: /var/propease/dataprotection-keys
```

- **volumes**：host path `/var/propease/dataprotection-keys` 挂载到 container 内同一路径
- 确保所有 container 实例共享同一套 keys

---

## 部署检查清单

### 云端服务器配置

#### ✅ 必做项

1. **创建共享存储目录**
   ```bash
   sudo mkdir -p /var/propease/dataprotection-keys
   sudo chmod 755 /var/propease/dataprotection-keys
   ```

2. **确保权限**
   ```bash
   sudo chown nobody:nogroup /var/propease/dataprotection-keys
   sudo chmod 755 /var/propease/dataprotection-keys
   ```

3. **如果有多个 EC2 实例**：
   - **方案 A (推荐)**：使用 AWS EFS，mount 到 `/var/propease/dataprotection-keys`
   - **方案 B**：使用 RDS/数据库持久化 DataProtection keys（需要额外配置）
   - **方案 C**：使用 Redis 持久化 keys（需要额外配置）
   - **不推荐**：Sticky Sessions 只能临时缓解，不是根治

### 本地开发配置

- 无需修改，使用相对路径 `DataProtection-Keys/`
- 本地开发自动使用 `builder.Environment.IsDevelopment()` 分支

---

## Nginx 配置示例

如果前面有 Nginx reverse proxy，确保以下配置：

```nginx
upstream propease_backend {
    server mymvcapp:8080;
}

server {
    listen 80;
    server_name _;
    
    # 重定向 HTTP 到 HTTPS
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl;
    server_name propease.dev;

    ssl_certificate /path/to/cert.pem;
    ssl_certificate_key /path/to/key.pem;

    location / {
        proxy_pass http://propease_backend;
        
        # CRITICAL: 转发代理头供 ForwardedHeaders 中间件读取
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-Host $host;
        proxy_set_header X-Forwarded-Port $server_port;
        
        # WebSocket 支持（如需要）
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        
        # 超时配置
        proxy_connect_timeout 60s;
        proxy_send_timeout 60s;
        proxy_read_timeout 60s;
    }
}
```

**关键头**：
- `X-Forwarded-Proto $scheme`：告知后端原始请求是 http 还是 https
- `X-Forwarded-For $proxy_add_x_forwarded_for`：传递客户端真实 IP
- `Host $host`：保持原始 host header

---

## 调试步骤

### 1. 运行应用并观察日志
```bash
docker-compose -f docker-compose.ec2.yml up -d
docker logs mymvcapp -f
```

### 2. 访问 CheckAuth 端点
```bash
curl -b cookies.txt -c cookies.txt https://propease.dev/Account/Login
# 登录后...
curl -b cookies.txt https://propease.dev/Account/CheckAuth
```

### 3. 查看日志输出
```
[AUTH_DEBUG] Machine=ip-172-31-0-100 | PID=123 | Host=propease.dev | Scheme=https | Path=/Account/CheckAuth | IsAuth=true | Name=user@example.com | HasIdentityCookie=true | Roles=Admin
```

### 4. 常见问题排查

| 症状 | 可能原因 | 解决 |
|------|--------|------|
| `IsAuth=false` 但 `HasIdentityCookie=true` | DataProtection keys 不同步 | 检查 volume mount，确保所有 instance 共享 keys |
| `Scheme=http` 但应该是 https | `X-Forwarded-Proto` 未被读取 | 检查 Nginx 配置，确保有 `proxy_set_header X-Forwarded-Proto $scheme` |
| Cookie 每次请求都丢失 | `CookieSecurePolicy=Always` 但 Scheme=http | 修复 Nginx 反向代理的 scheme 转发 |
| 多个 container 时认证不稳定 | DataProtection keys 存在各自 container 内 | 使用 volume mount 到共享存储 |

---

## 总结

**核心修复**：
1. ✅ DataProtection keys 持久化到 `/var/propease/dataprotection-keys`
2. ✅ Forwarded Headers 配置，识别 HTTPS
3. ✅ Identity Cookie 显式配置，确保参数一致
4. ✅ Docker volume mount，所有 instance 共享 keys
5. ✅ Debug middleware 帮助诊断

**部署要点**：
- `/var/propease/dataprotection-keys` **必须** 是共享存储（EFS、NFS、shared volume）
- 若是 Kubernetes，使用 `emptyDir` 或 `persistentVolumeClaim`
- 若是多个 EC2 实例，使用 EFS；若是单 EC2，host folder mount 即可

**验证**：
- 登录后刷新 `/Account/CheckAuth`，应该始终返回 `isAuthenticated=true`
- 查看日志确认 `Scheme=https` 和 `IsAuth=true`
- 多次请求 `/Admin/Dashboard` 应该不被重定向
