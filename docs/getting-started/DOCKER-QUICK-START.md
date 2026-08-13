# Docker Quick Start Guide

**Quick reference for deploying Sorcha with Docker Compose**

---

## Prerequisites

- **Docker Desktop** 20.10+ with Docker Compose 2.0+
- **Git** (to clone the repository)
- **.NET 10 SDK** (optional — only needed if you want HTTPS on port 443)

---

## Setup Steps

### 1. Clone the Repository

```bash
git clone https://github.com/sorcha-platform/sorcha.git
cd Sorcha
```

### 2. Generate HTTPS Certificate (Optional)

The API Gateway listens on **HTTP port 80** by default. HTTPS on port 443 is also configured but requires a certificate. If you skip this step, HTTP works normally and the HTTPS listener is the only thing that fails to bind.

To enable HTTPS, generate a development certificate before starting services:

```bash
# Create certificates directory
mkdir -p docker/certs

# Generate development certificate (requires .NET 10 SDK)
dotnet dev-certs https -ep docker/certs/aspnetapp.pfx -p SorchaDev2025 --trust
```

Or use the provided script (Windows):

```powershell
.\scripts\generate-dev-cert.ps1
```

### 3. Start Services

Run the setup script first — it generates the per-deploy secrets (including the 8
service-to-service `ServiceAuth` secrets) into `.env` before starting the stack. A bare
`docker-compose up` without a generated `.env` fails by design (see main [README Quick
Start](../../README.md#quick-start)):

```bash
# Interactive setup — generates .env, pulls images, starts services
./scripts/sorcha-setup.sh

# Once .env exists, you can also drive compose directly:
docker-compose up -d

# View logs (optional)
docker-compose logs -f
```

### 4. Verify Deployment

Wait 30-60 seconds for all services to start, then access:

```bash
# Health check (should return healthy status)
curl http://localhost/health

# Main UI
open http://localhost/app

# API documentation
open http://localhost/openapi

# Aspire observability dashboard
open http://localhost:18888
```

---

## Access Points

### Main Entry Points

| Service | URL | Description |
|---------|-----|-------------|
| **Main UI** | `http://localhost/app` | Sorcha web UI |
| **API Gateway** | `http://localhost/` | Primary HTTP entry point (port 80) |
| **API Gateway (HTTPS)** | `https://localhost/` | HTTPS entry point — requires certificate (see Step 2) |
| **API Documentation** | `http://localhost/openapi` | OpenAPI / Scalar docs |
| **Health Check** | `http://localhost/health` | Aggregated gateway health |
| **Aspire Dashboard** | `http://localhost:18888` | Traces, logs, and metrics |

### Infrastructure

| Service | URL | Credentials |
|---------|-----|-------------|
| **PostgreSQL** | `localhost:5432` | User: `sorcha`, Password: `sorcha_dev_password` |
| **MongoDB** | `localhost:27017` | User: `sorcha`, Password: `sorcha_dev_password` |
| **Redis** | `localhost:16379` (→ 6379 in-container) | No authentication |
| **Aspire Dashboard** | `http://localhost:18888` | Observability dashboard |

### P2P Network

| Service | URL | Purpose |
|---------|-----|---------|
| **Hub Node (gRPC)** | `localhost:50051` | P2P hub for external connections |
| **Peer Service (gRPC)** | `localhost:50052` | Regular peer connections |

---

## Common Operations

### View Logs

```bash
# All services
docker-compose logs -f

# Specific service
docker-compose logs -f api-gateway
docker-compose logs -f wallet-service

# Last 100 lines
docker-compose logs --tail=100 api-gateway
```

### Check Service Status

```bash
# List all services
docker-compose ps

# Check for unhealthy services
docker-compose ps | grep unhealthy

# View resource usage
docker stats
```

### Restart Services

```bash
# Restart all services
docker-compose restart

# Restart specific service
docker-compose restart api-gateway

# Rebuild and restart
docker-compose up -d --build
```

### Stop Services

```bash
# Stop all services (preserve data)
docker-compose stop

# Stop and remove containers (preserve data in volumes)
docker-compose down

# Stop and remove everything including volumes (⚠️ deletes all data)
docker-compose down -v
```

---

## Troubleshooting

### HTTPS Certificate Missing

**Error:** API Gateway logs show it cannot bind HTTPS (certificate not found at `/https/aspnetapp.pfx`)

This only affects port 443. HTTP on port 80 continues to work. If you need HTTPS:

```bash
mkdir -p docker/certs
dotnet dev-certs https -ep docker/certs/aspnetapp.pfx -p SorchaDev2025 --trust
docker-compose restart api-gateway
```

### Port Already in Use

**Error:** `Bind for 0.0.0.0:80 failed: port is already allocated`

**Solution:** Edit `docker-compose.yml` and change the port mapping:
```yaml
api-gateway:
  ports:
    - "8080:8080"  # Changed from 80:8080
```

Then restart:
```bash
docker-compose down
docker-compose up -d
```

Access via `http://localhost:8080/` instead.

### Redis Connection Errors

**Error:** Services can't connect to Redis

**Solution:**
```bash
# Check Redis health
docker-compose ps redis

# Restart Redis
docker-compose restart redis

# Verify Redis is responding
docker-compose exec redis redis-cli ping
# Should return: PONG
```

### Service Won't Start

**Check logs:**
```bash
# View service logs
docker-compose logs <service-name>

# Examples:
docker-compose logs api-gateway
docker-compose logs wallet-service
docker-compose logs postgres
```

**Common issues:**
- **PostgreSQL not ready**: Wait 30-60 seconds for database initialization
- **MongoDB not ready**: Wait for health check to pass
- **Network issues**: Restart Docker Desktop
- **Out of memory**: Increase Docker Desktop memory allocation

### Verify Internal Networking

```bash
# Test if API Gateway can reach backend services
docker exec sorcha-api-gateway curl http://wallet-service:8080/health
docker exec sorcha-api-gateway curl http://blueprint-service:8080/health

# Check Docker network
docker network inspect sorcha_sorcha-network
```

---

## Testing the Deployment

### API Gateway Endpoints

```bash
# Health check
curl http://localhost/health

# OpenAPI schema
curl http://localhost/openapi/v1.json
```

### Backend Service Routing

All backend services are accessed through the API Gateway:

```bash
# Blueprint Service
curl http://localhost/api/blueprints

# Wallet Service
curl http://localhost/api/wallets

# Register Service
curl http://localhost/api/register

# Tenant Service (Auth)
curl http://localhost/api/tenants
```

### gRPC Services

```bash
# List gRPC services on Hub Node
grpcurl -plaintext localhost:50051 list

# Expected output:
# grpc.reflection.v1alpha.ServerReflection
# sorcha.peer.discovery.PeerDiscovery
# sorcha.peer.v1.Heartbeat
# sorcha.peer.v1.HubNodeConnection
# sorcha.peer.v1.SystemRegisterSync
```

---

## Data Persistence

Docker volumes are used to persist data:

```bash
# List volumes
docker volume ls | grep sorcha

# Expected volumes:
# sorcha_postgres-data
# sorcha_mongodb-data
# sorcha_redis-data
# sorcha_dataprotection-keys
```

### Backup Data

```bash
# Backup PostgreSQL
docker exec sorcha-postgres pg_dumpall -U sorcha > backup-postgres.sql

# Backup MongoDB
docker exec sorcha-mongodb mongodump --archive=backup-mongodb.archive

# Backup Redis
docker run --rm -v sorcha_redis-data:/data -v $(pwd):/backup \
  alpine tar czf /backup/redis-backup.tar.gz /data
```

### Restore Data

```bash
# Restore PostgreSQL
cat backup-postgres.sql | docker exec -i sorcha-postgres psql -U sorcha

# Restore MongoDB
cat backup-mongodb.archive | docker exec -i sorcha-mongodb mongorestore --archive

# Restore Redis
docker run --rm -v sorcha_redis-data:/data -v $(pwd):/backup \
  alpine tar xzf /backup/redis-backup.tar.gz -C /
```

---

## Network Architecture

Sorcha uses a **single bridge network** with port publishing:

- **Internal Communication**: Services use Docker DNS (e.g., `http://wallet-service:8080`)
- **External Access**: Only published ports are accessible from host
- **API Gateway**: Centralized HTTP/HTTPS ingress for all backend services
- **gRPC Direct**: Peer services exposed directly for P2P communication

See the [Network Architecture](#network-architecture) section above for detailed architecture.

---

## Next Steps

### Development

1. **Explore the API**:
   - Open `http://localhost/scalar/` for interactive API documentation
   - Test endpoints using the Scalar UI

2. **View Metrics**:
   - Open `http://localhost:18888` for Aspire Dashboard
   - Monitor service health, traces, and logs

3. **Connect to Databases**:
   - PostgreSQL: Use any database client with `localhost:5432`
   - MongoDB: Use MongoDB Compass with `localhost:27017`
   - Redis: Use Redis Commander or `redis-cli`

### Production Deployment

For production deployments:

1. **Replace HTTPS Certificate**:
   - Use CA-signed certificates (Let's Encrypt, DigiCert, etc.)
   - Update `docker-compose.yml` with production certificate paths

2. **Secure Infrastructure**:
   - Enable Redis authentication
   - Use strong PostgreSQL/MongoDB passwords
   - Restrict port access via firewall rules

3. **Configure Environment**:
   - Set `ASPNETCORE_ENVIRONMENT=Production`
   - Configure proper JWT signing keys
   - Use Azure Key Vault or AWS KMS for secrets

See [DEPLOYMENT.md](../guides/DEPLOYMENT.md) for full production deployment guide.

---

## Additional Resources

- **Main README**: [../README.md](../README.md)
- **Deployment Guide**: [DEPLOYMENT.md](../guides/DEPLOYMENT.md)
- **Docker Networking**: See [Network Architecture](#network-architecture) section above
- **Port Configuration**: [PORT-CONFIGURATION.md](PORT-CONFIGURATION.md)
- **Architecture**: [architecture.md](../architecture.md)

---

## Summary

**Quick Commands:**
```bash
# Start (HTTP only — no certificate needed)
docker-compose up -d

# Access
open http://localhost/app

# Logs
docker-compose logs -f

# Stop
docker-compose down
```

**Access Points:**
- Main UI: `http://localhost/app`
- API Docs: `http://localhost/openapi`
- Health: `http://localhost/health`
- Aspire Dashboard: `http://localhost:18888`

**Need Help?**
- Check logs: `docker-compose logs <service>`
- View status: `docker-compose ps`
- See [Troubleshooting](#troubleshooting) section above
