# 部署步骤指南 - Identity Cookie 修复

## 前置检查

```bash
# 1. 确认 AWS 凭证配置
aws sts get-caller-identity

# 2. 检查 S3 bucket 和 Cognito 状态
aws s3 ls s3://propease-community-images-2026 --region ap-southeast-1
aws cognito-idp describe-user-pool --user-pool-id <your-pool-id> --region ap-southeast-1
```

---

## EC2 部署步骤

### 步骤 1：准备 EC2 实例

```bash
# SSH 进入 EC2 实例
ssh -i your-key.pem ubuntu@your-ec2-ip

# 更新系统
sudo apt update && sudo apt upgrade -y

# 安装 Docker 和 Docker Compose（如未安装）
sudo apt install -y docker.io docker-compose
sudo usermod -aG docker $USER
newgrp docker

# 创建持久化数据目录
sudo mkdir -p /var/propease/dataprotection-keys
sudo chmod 755 /var/propease/dataprotection-keys

# 如果是多实例部署，使用 EFS
# 挂载 EFS（假设 EFS mount target 在 az-a）
sudo apt install -y nfs-common
sudo mkdir -p /mnt/efs
sudo mount -t nfs4 -o nfsvers=4.1,rsize=1048576,wsize=1048576,hard,timeo=600,retrans=2 \
    fs-xxxxxxxx.efs.ap-southeast-1.amazonaws.com:/ /mnt/efs

# 创建符号链接
sudo ln -s /mnt/efs/dataprotection-keys /var/propease/dataprotection-keys
```

### 步骤 2：准备应用配置

```bash
# 在 EC2 创建应用目录
mkdir -p ~/propease-app
cd ~/propease-app

# 复制 docker-compose.ec2.yml（已包含 DATAPROTECTION_KEYS_PATH 和 volume mount）
# 从本地开发环境 SCP 或 git clone

# 更新 appsettings.Production.json（如需要）
cat > appsettings.Production.json << 'EOF'
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=your-rds-endpoint;Database=propease_db;User Id=admin;Password=<password>"
  },
  "AWS": {
    "S3BucketName": "propease-community-images-2026",
    "Region": "ap-southeast-1"
  },
  "Cognito": {
    "Authority": "https://cognito-idp.ap-southeast-1.amazonaws.com/<pool-id>",
    "ClientId": "<client-id>",
    "ClientSecret": "<client-secret>"
  }
}
EOF
```

### 步骤 3：启动应用

```bash
# 构建镜像（第一次）
docker build -f MyMvcApp/Dockerfile -t propease-app:latest .

# 或使用 docker-compose 直接启动（自动构建）
docker-compose -f docker-compose.ec2.yml up -d

# 验证容器运行
docker ps
docker logs mymvcapp -f --tail=50
```

### 步骤 4：验证修复

```bash
# 检查 DataProtection keys 目录
ls -la /var/propease/dataprotection-keys/
# 应该看到 keys 文件被创建

# 访问调试端点
curl -v https://propease.dev/Account/CheckAuth
# 应该看到 200 OK 和 auth debug 信息

# 检查日志中的 AUTH_DEBUG 输出
docker logs mymvcapp | grep "AUTH_DEBUG"
```

---

## 多实例部署（使用 AWS ECS）

### 步骤 1：创建 ECS Task Definition

```json
{
  "family": "propease-task",
  "networkMode": "awsvpc",
  "requiresCompatibilities": ["FARGATE"],
  "cpu": "256",
  "memory": "512",
  "containerDefinitions": [
    {
      "name": "propease-app",
      "image": "<account-id>.dkr.ecr.ap-southeast-1.amazonaws.com/propease:latest",
      "portMappings": [
        {
          "containerPort": 8080,
          "hostPort": 8080,
          "protocol": "tcp"
        }
      ],
      "environment": [
        {
          "name": "ASPNETCORE_ENVIRONMENT",
          "value": "Production"
        },
        {
          "name": "DATAPROTECTION_KEYS_PATH",
          "value": "/var/propease/dataprotection-keys"
        }
      ],
      "mountPoints": [
        {
          "sourceVolume": "dataprotection-keys",
          "containerPath": "/var/propease/dataprotection-keys",
          "readOnly": false
        }
      ],
      "logConfiguration": {
        "logDriver": "awslogs",
        "options": {
          "awslogs-group": "/ecs/propease",
          "awslogs-region": "ap-southeast-1",
          "awslogs-stream-prefix": "ecs"
        }
      }
    }
  ],
  "volumes": [
    {
      "name": "dataprotection-keys",
      "efsVolumeConfiguration": {
        "filesystemId": "fs-xxxxxxxx",
        "transitEncryption": "ENABLED"
      }
    }
  ]
}
```

### 步骤 2：创建 ECS Service

```bash
# 在 AWS Console 或使用 AWS CLI 创建 ECS Service
# 确保：
# 1. 任务数量 = 1（初始）或多个（需要粘性会话或共享 EFS）
# 2. EFS 文件系统已关联
# 3. 负载均衡器（ALB）已配置，转发 HTTPS

aws ecs create-service \
  --cluster propease-cluster \
  --service-name propease-service \
  --task-definition propease-task:1 \
  --desired-count 2 \
  --launch-type FARGATE \
  --network-configuration "awsvpcConfiguration={subnets=[subnet-xxx],securityGroups=[sg-xxx],assignPublicIp=ENABLED}" \
  --load-balancers targetGroupArn=arn:aws:elasticloadbalancing:...,containerName=propease-app,containerPort=8080 \
  --region ap-southeast-1
```

### 步骤 3：配置 ALB（Application Load Balancer）

```bash
# 在 AWS Console 中配置 ALB：

1. Listener：
   - HTTP (80) → Redirect to HTTPS (443)
   - HTTPS (443) → Forward to Target Group

2. Target Group：
   - Health Check: /Account/CheckAuth
   - Health Check Interval: 30 seconds
   - Stickiness: Optional（如果启用，确保 idle timeout < session duration）

3. 不需要粘性会话，因为 DataProtection keys 在 EFS 上是共享的
```

---

## Nginx 配置部署

如果使用 Nginx 作为反向代理：

```bash
# 在 EC2 安装 Nginx
sudo apt install -y nginx

# 复制配置文件
sudo cp nginx.conf.example /etc/nginx/sites-available/propease
sudo ln -s /etc/nginx/sites-available/propease /etc/nginx/sites-enabled/

# 测试配置
sudo nginx -t

# 启动 Nginx
sudo systemctl restart nginx

# 查看日志
sudo tail -f /var/log/nginx/propease_access.log
sudo tail -f /var/log/nginx/propease_error.log
```

---

## 监控和故障排查

### 实时监控日志

```bash
# 容器日志
docker logs -f mymvcapp | grep -i "auth\|error\|exception"

# 或使用 CloudWatch
aws logs tail /ecs/propease --follow

# 查看特定时间段的日志
aws logs filter-log-events \
  --log-group-name /ecs/propease \
  --query 'events[*].[timestamp,message]' \
  --output text | tail -20
```

### 调试不稳定的认证

```bash
# 1. 登录
curl -X POST https://propease.dev/Account/Login \
  -d "Email=user@example.com&Password=password" \
  -c cookies.txt

# 2. 检查 Auth 状态
curl -b cookies.txt https://propease.dev/Account/CheckAuth

# 3. 多次请求确保一致性
for i in {1..5}; do
  curl -b cookies.txt https://propease.dev/Account/CheckAuth
  sleep 1
done

# 4. 查看容器日志中的 AUTH_DEBUG 输出
docker logs mymvcapp | grep AUTH_DEBUG | head -20
```

### 常见问题排查

| 问题 | 症状 | 检查项目 |
|------|------|--------|
| DataProtection keys 不同步 | 随机 302 到 Login | `ls -la /var/propease/dataprotection-keys/` 应该有多个 key 文件 |
| Scheme 始终是 http | cookie 被拒绝 | 检查 Nginx `X-Forwarded-Proto $scheme` 配置 |
| 间歇性登出 | 不稳定的认证 | 验证 volume mount 生效：`docker inspect mymvcapp \| grep Mounts -A 10` |
| 日志中 auth error | 无法解密 cookie | 检查 `/var/propease/dataprotection-keys` 权限：`sudo chmod 755 /var/propease/dataprotection-keys` |

---

## 回滚计划

如果遇到问题，快速回滚：

```bash
# 1. 停止当前容器
docker-compose -f docker-compose.ec2.yml down

# 2. 恢复旧的 docker-compose.yml（不包含 DATAPROTECTION_KEYS_PATH）
git checkout HEAD^ -- docker-compose.ec2.yml

# 3. 重启
docker-compose -f docker-compose.ec2.yml up -d

# 4. 如果需要调查，保存日志
docker logs mymvcapp > /tmp/failure_logs.txt
```

---

## 验证清单

在生产环境中部署前，确保：

- [ ] `/var/propease/dataprotection-keys` 目录已创建并有正确权限
- [ ] docker-compose.ec2.yml 包含 `DATAPROTECTION_KEYS_PATH` 环境变量和 volume mount
- [ ] Dockerfile 已更新，包含 `mkdir -p /var/propease/dataprotection-keys`
- [ ] Program.cs 已应用所有修复：DataProtection、ForwardedHeaders、Cookie 配置、debug middleware
- [ ] Nginx 配置包含 `X-Forwarded-Proto $scheme` 等代理头
- [ ] SSL 证书已正确安装（或使用 AWS Certificate Manager）
- [ ] 应用能够启动，无编译错误（`dotnet build` 成功）
- [ ] `/Account/CheckAuth` 端点可访问
- [ ] 登录后刷新页面，认证状态保持一致
- [ ] 日志中出现 `[AUTH_DEBUG]` 条目
- [ ] CloudWatch/ECS logs 中无 DataProtection 相关错误
