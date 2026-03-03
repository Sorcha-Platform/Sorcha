# Infrastructure Setup Guide

**Version:** 1.0
**Last Updated:** 2025-12-12
**Purpose:** Local development infrastructure setup for Sorcha platform

---

## Overview

Sorcha requires the following infrastructure services:
- **PostgreSQL 17** - Relational database for Tenant and Wallet services
- **Redis 8** - Cache and session storage for all services
- **MongoDB 8** - Document database for Register service

This guide covers setting up these services for local development.

---

## Prerequisites

- Docker Desktop installed and running
- Docker Compose v2.x or later
- .NET 10 SDK installed
- At least 4GB RAM available for containers

---

## Quick Start (Infrastructure Only)

For local development without Docker images for all services:

```bash
# Start infrastructure services
docker-compose -f docker-compose.infrastructure.yml up -d

# Check status
docker-compose -f docker-compose.infrastructure.yml ps

# View logs
docker-compose -f docker-compose.infrastructure.yml logs -f

# Stop services
docker-compose -f docker-compose.infrastructure.yml down
```

**This starts:**
- PostgreSQL on `localhost:5432`
- Redis on `localhost:6379`
- MongoDB on `localhost:27017`

**Databases created:**
- `sorcha` (default PostgreSQL database for Wallet Service)
- `sorcha_tenant` (PostgreSQL database for Tenant Service)

---

## Full Stack (All Services)

To run the complete Sorcha platform in Docker:

```bash
# Build and start all services
docker-compose up --build -d

# Check status
docker-compose ps

# View logs for specific service
docker-compose logs -f tenant-service

# Stop all services
docker-compose down
```

**Services started:**
- Infrastructure (PostgreSQL, Redis, MongoDB)
- Tenant Service on `localhost:5110`
- Blueprint Service on `localhost:5000`
- Wallet Service on `localhost:5001`
- Register Service on `localhost:5290`
- Peer Service on `localhost:5002`
- API Gateway on `localhost:8080`
- Aspire Dashboard on `localhost:18888`

---

## Database Credentials

**PostgreSQL:**
```
Host: localhost
Port: 5432
Database: sorcha / sorcha_tenant
Username: sorcha
Password: sorcha_dev_password
```

**MongoDB:**
```
Host: localhost
Port: 27017
Username: sorcha
Password: sorcha_dev_password
```

**Redis:**
```
Host: localhost
Port: 6379
(No authentication in development)
```

⚠️ **Security Warning:** These are development credentials only. Never use these in production!

---

## Connection Strings

### For .NET Services (appsettings.Development.json)

**PostgreSQL (Tenant Service):**
```json
{
  "ConnectionStrings": {
    "TenantDatabase": "Host=localhost;Port=5432;Database=sorcha_tenant;Username=sorcha;Password=sorcha_dev_password;Include Error Detail=true"
  }
}
```

**PostgreSQL (Wallet Service):**
```json
{
  "ConnectionStrings": {
    "wallet-db": "Host=localhost;Port=5432;Database=sorcha;Username=sorcha;Password=sorcha_dev_password"
  }
}
```

**Redis:**
```json
{
  "Redis": {
    "ConnectionString": "localhost:6379"
  }
}
```

**MongoDB (Register Service):**
```json
{
  "ConnectionStrings": {
    "MongoDB": "mongodb://sorcha:sorcha_dev_password@localhost:27017"
  }
}
```

---

## Testing Database Connectivity

### PostgreSQL

```bash
# Using Docker exec
docker exec -it sorcha-postgres psql -U sorcha -d sorcha_tenant

# List databases
\l

# Connect to tenant database
\c sorcha_tenant

# List tables
\dt

# Exit
\q
```

### Redis

```bash
# Using Docker exec
docker exec -it sorcha-redis redis-cli

# Test ping
PING

# List keys
KEYS *

# Exit
EXIT
```

### MongoDB

```bash
# Using Docker exec
docker exec -it sorcha-mongodb mongosh -u sorcha -p sorcha_dev_password

# Show databases
show dbs

# Use database
use sorcha_registers

# Show collections
show collections

# Exit
exit
```

---

## Troubleshooting

### Port Already in Use

```bash
# Windows PowerShell
netstat -ano | findstr :5432
taskkill /PID <pid> /F

# Linux/macOS
lsof -i :5432
kill -9 <pid>
```

### Container Won't Start

```bash
# Check Docker Desktop is running
docker info

# Remove old volumes and restart
docker-compose -f docker-compose.infrastructure.yml down -v
docker-compose -f docker-compose.infrastructure.yml up -d
```

### Database Connection Timeout

**Symptom:** .NET services can't connect to PostgreSQL from host

**Possible Causes:**
1. Docker Desktop networking issue on Windows
2. Firewall blocking localhost connections
3. Containers not fully started

**Solutions:**
```bash
# 1. Verify container is healthy
docker-compose -f docker-compose.infrastructure.yml ps

# 2. Check PostgreSQL logs
docker logs sorcha-postgres

# 3. Restart Docker Desktop

# 4. Use Docker host.docker.internal (Windows/Mac)
# In appsettings.Development.json:
"TenantDatabase": "Host=host.docker.internal;Port=5432;..."
```

### Permission Denied on Init Script

```bash
# Linux/macOS only
chmod +x scripts/init-databases.sql
```

---

## Data Persistence

Docker volumes are used for data persistence:
- `postgres-data` - PostgreSQL database files
- `redis-data` - Redis RDB snapshots
- `mongodb-data` - MongoDB database files

**To reset all data:**
```bash
docker-compose -f docker-compose.infrastructure.yml down -v
```

⚠️ **Warning:** This deletes ALL database data including users and service principals!

---

## Health Checks

All infrastructure services have built-in health checks:

```bash
# Check health status
docker inspect sorcha-postgres | grep -A 10 Health
docker inspect sorcha-redis | grep -A 10 Health
docker inspect sorcha-mongodb | grep -A 10 Health
```

**Healthy containers will show:**
- PostgreSQL: `pg_isready -U sorcha` returns 0
- Redis: `redis-cli ping` returns PONG
- MongoDB: `mongosh ping` returns success

---

## Production Considerations

For production deployment:

1. ✅ **Never use development passwords**
   - Use Azure Key Vault, AWS Secrets Manager, or similar
   - Rotate passwords regularly

2. ✅ **Use managed services when possible**
   - Azure Database for PostgreSQL
   - Azure Cache for Redis
   - Azure Cosmos DB (MongoDB API)

3. ✅ **Enable SSL/TLS connections**
   - PostgreSQL: `sslmode=require`
   - Redis: TLS enabled
   - MongoDB: TLS/SSL enabled

4. ✅ **Configure backups**
   - Automated daily backups
   - Point-in-time recovery
   - Test restore procedures

5. ✅ **Monitor resource usage**
   - CPU, memory, disk utilization
   - Connection pool sizes
   - Query performance

6. ✅ **Network security**
   - Private virtual networks
   - No public IPs for databases
   - Firewall rules limiting access

---

## Next Steps

After starting infrastructure:

1. **Run Tenant Service** to create default organization and admin user
   ```bash
   dotnet run --project src/Services/Sorcha.Tenant.Service
   ```

2. **Bootstrap seeding runs automatically** creating:
   - Default organization: `Sorcha Local` (ID: `00000000-0000-0000-0000-000000000001`)
   - Admin user: `admin@sorcha.local` / `Dev_Pass_2025!`
   - 4 service principals: Blueprint, Wallet, Register, Peer services

3. **Service credentials are shown in logs** (WARNING level):
   ```
   Service Principal Created - Blueprint Service
     Client ID:     service-blueprint
     Client Secret: s5CeyuJs9tRtBnPIElPesrRsBhqvyYRtaxmAineg01w
     Scopes:        blueprints:read, blueprints:write, wallets:sign, register:write
     ⚠️  SAVE THIS SECRET - It will not be shown again!
   ```

4. **Credentials are saved automatically** to `.env.local` (gitignored)
   - See [BOOTSTRAP-CREDENTIALS.md](BOOTSTRAP-CREDENTIALS.md) for complete credential reference

5. **Test authentication**:
   - User login: [AUTHENTICATION-SETUP.md](../guides/AUTHENTICATION-SETUP.md)
   - Service tokens: [BOOTSTRAP-CREDENTIALS.md](BOOTSTRAP-CREDENTIALS.md#service-principal-credentials)

---

## References

- [Docker Compose Documentation](https://docs.docker.com/compose/)
- [PostgreSQL Docker Image](https://hub.docker.com/_/postgres)
- [Redis Docker Image](https://hub.docker.com/_/redis)
- [MongoDB Docker Image](https://hub.docker.com/_/mongo)
- [.NET Aspire Documentation](https://learn.microsoft.com/dotnet/aspire/)

---

**Document Version:** 1.1
**Last Updated:** 2025-12-13
**Owner:** Sorcha Architecture Team
**Status:** ✅ Infrastructure deployed, tested, and bootstrap seeding verified
