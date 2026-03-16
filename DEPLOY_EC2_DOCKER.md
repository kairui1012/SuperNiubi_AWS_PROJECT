# Deploy MyMvcApp to EC2 with Docker

## 1. EC2 security group

Open these inbound ports on your EC2 security group:

- 22 (SSH)
- 80 (HTTP)

## 2. Install Docker and Compose plugin (Amazon Linux 2023)

```bash
sudo dnf update -y
sudo dnf install -y docker
sudo systemctl enable --now docker
sudo usermod -aG docker ec2-user
newgrp docker

DOCKER_CONFIG=${DOCKER_CONFIG:-$HOME/.docker}
mkdir -p "$DOCKER_CONFIG/cli-plugins"
curl -SL https://github.com/docker/compose/releases/download/v2.29.7/docker-compose-linux-x86_64 \
  -o "$DOCKER_CONFIG/cli-plugins/docker-compose"
chmod +x "$DOCKER_CONFIG/cli-plugins/docker-compose"
docker compose version
```

## 3. Upload project and run

In the project root (`dotNET`):

```bash
docker compose -f docker-compose.ec2.yml up -d --build
```

## 4. Verify

```bash
docker ps
curl -I http://localhost
```

Then visit:

`http://<EC2_PUBLIC_IP>`

## 5. Common operations

```bash
# view logs
docker compose -f docker-compose.ec2.yml logs -f

# restart
docker compose -f docker-compose.ec2.yml restart

# stop and remove
docker compose -f docker-compose.ec2.yml down
```

## 6. Optional: use your own domain + HTTPS

You can put Nginx/Caddy in front (or use ALB) and keep this container on internal HTTP.