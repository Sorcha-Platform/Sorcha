# Port Configuration Guide

**Version:** 1.0
**Last Updated:** 2025-12-15
**Status:** Active

---

## Overview

Sorcha uses a standardized port configuration across all development and deployment environments to ensure consistency and ease of use. This document defines the canonical port assignments for all services.

## Standardized Port Scheme

### Design Principles

1. **Consistency**: Same external ports across local development (Aspire) and Docker environments
2. **Predictability**: Fixed, well-known ports for each service
3. **Non-conflicting**: Ports chosen to avoid common conflicts with other development tools
4. **Environment-specific**: HTTP for development, HTTPS for production

### Port Assignments

| Service | HTTP Port | HTTPS Port | Docker External | Docker Internal |
|---------|-----------|------------|-----------------|-----------------|
| **Blueprint Service** | 5000 | 7000 | 5000 | 8080 |
| **Wallet Service** | 5001 | 7001 | 5001 | 8080 |
| **Peer Service** | 5002 | 7002 | 5002 | 8080 |
| **Tenant Service** | 5110 | 7110 | 5110 | 8080 |
| **Register Service** | 5290 | 7290 | 5290 | 8080 |
| **API Gateway** | 8080 | 7082 | 8080 | 8080 |
| **Admin UI** | 8081 | 7083 | N/A | N/A |

### Infrastructure Services

| Service | Port | Purpose |
|---------|------|---------|
| **PostgreSQL** | 5432 | Database server |
| **MongoDB** | 27017 | Document database |
| **Redis** | 6379 | Caching and message broker |
| **Aspire Dashboard** | 18888 | Observability dashboard |
| **Aspire OTLP gRPC** | 4317 (18889 internal) | Telemetry collection |
| **Aspire OTLP HTTP** | 4318 (18890 internal) | Telemetry collection |

---

## Environment-Specific Configuration

### 1. Local Development (Aspire/AppHost)

**Environment:** Local development with .NET Aspire orchestration

**Characteristics:**
- HTTPS with self-signed certificates
- Full observability with Aspire Dashboard
- Individual service endpoints exposed
- Suitable for service-to-service debugging

**Service URLs:**
```
Tenant Service:    https://localhost:7110
Blueprint Service: https://localhost:7000
Wallet Service:    https://localhost:7001
Register Service:  https://localhost:7290
Peer Service:      https://localhost:7002
API Gateway:       https://localhost:7082
Admin UI:          https://localhost:7083
Aspire Dashboard:  http://localhost:18888
```

**Authentication:**
```bash
# Service-to-service authentication
POST https://localhost:7110/api/service-auth/token

# User authentication
POST https://localhost:7110/api/auth/login
```

**Configuration:**
- **AppHost**: `src/Apps/Sorcha.AppHost/AppHost.cs`
- **Admin Client**: Profile "local" in `ProfileDefaults.cs`
- **CLI**: Profile "local" in `config.template.json`

**How to Start:**
```bash
cd src/Apps/Sorcha.AppHost
dotnet run
```

---

### 2. Docker Compose

**Environment:** Containerized development with docker-compose

**Characteristics:**
- HTTP only (no TLS)
- All services behind API Gateway
- Suitable for integration testing and demos
- Matches production routing pattern

**Service URLs (via Gateway):**
```
API Gateway:       http://localhost:8080

Tenant Service:    http://localhost:8080/tenant
Blueprint Service: http://localhost:8080/blueprint
Wallet Service:    http://localhost:8080/wallet
Register Service:  http://localhost:8080/register
Peer Service:      http://localhost:8080/peer
```

**Direct Service URLs (bypassing gateway):**
```
Tenant Service:    http://localhost:5110
Blueprint Service: http://localhost:5000
Wallet Service:    http://localhost:5001
Register Service:  http://localhost:5290
Peer Service:      http://localhost:5002
```

**Authentication:**
```bash
# Via Gateway
POST http://localhost:8080/tenant/api/service-auth/token
POST http://localhost:8080/tenant/api/auth/login

# Direct to Tenant Service
POST http://localhost:5110/api/service-auth/token
POST http://localhost:5110/api/auth/login
```

**Configuration:**
- **Docker Compose**: `docker-compose.yml`
- **Admin Client**: Profile "docker" in `ProfileDefaults.cs`
- **CLI**: Profile "docker" in `config.template.json`

**How to Start:**
```bash
docker-compose up -d
```

**Infrastructure UIs:**
```
Aspire Dashboard:  http://localhost:18888
pgAdmin:           http://localhost:5050    (if using docker-compose.infrastructure.yml)
Mongo Express:     http://localhost:8081    (if using docker-compose.infrastructure.yml)
Redis Commander:   http://localhost:8082    (if using docker-compose.infrastructure.yml)
```

---

### 3. Production

**Environment:** Production deployment (Azure Container Apps, Kubernetes, etc.)

**Characteristics:**
- HTTPS only (TLS 1.2+)
- Custom domain names
- Standard HTTPS port (443)
- Services behind reverse proxy/API Gateway

**Service URLs:**
```
Tenant Service:    https://tenant.sorcha.io
Blueprint Service: https://blueprint.sorcha.io
Wallet Service:    https://wallet.sorcha.io
Register Service:  https://register.sorcha.io
Peer Service:      https://peer.sorcha.io
```

**Authentication:**
```bash
POST https://tenant.sorcha.io/api/service-auth/token
POST https://tenant.sorcha.io/api/auth/login
```

**Configuration:**
- **Admin Client**: Profile "production" in `ProfileDefaults.cs`
- **CLI**: Profile "production" in `config.template.json`
- **Deployment**: Environment-specific configuration (Azure, K8s, etc.)

---

## Client Configuration

### Admin UI (Blazor WebAssembly)

**Profile Configuration:** `src/Apps/Sorcha.Admin/Models/Configuration/ProfileDefaults.cs`

The Admin UI supports three profiles:

| Profile | Environment | Default? | TLS? |
|---------|-------------|----------|------|
| `local` | Aspire/AppHost | ✅ Yes | ✅ Self-signed |
| `docker` | Docker Compose | ❌ No | ❌ HTTP only |
| `production` | Production | ❌ No | ✅ Valid cert |

**Switching Profiles:**
Profiles are selected at login and stored in browser local storage.

### CLI Tool

**Profile Configuration:** `src/Apps/Sorcha.Cli/config.template.json`

The CLI supports the same three profiles with identical service URLs.

**Switching Profiles:**
```bash
# View current profile
sorcha config get activeProfile

# Switch to docker profile
sorcha config set activeProfile docker

# Use a specific profile for a single command
sorcha --profile docker tenant list-organizations
```

---

## Port Selection Rationale

### Service Ports (5000-5300 range)

- **5000** (Blueprint): Standard HTTP development port
- **5001** (Wallet): Follows .NET default HTTPS port convention (5001)
- **5002** (Peer): Sequential allocation after Wallet
- **5110** (Tenant): Non-conflicting, memorable port
- **5290** (Register): Non-conflicting, memorable port

### HTTPS Ports (7000-7300 range)

- **7000** (Blueprint): Aligns with HTTP port 5000
- **7001** (Wallet): Aligns with HTTP port 5001
- **7002** (Peer): Aligns with HTTP port 5002
- **7082** (API Gateway): Common alternative HTTPS port
- **7083** (Admin UI): Sequential allocation after gateway
- **7110** (Tenant): Aligns with HTTP port 5110
- **7290** (Register): Aligns with HTTP port 5290

### Gateway Ports

- **8080** (HTTP): Industry-standard alternative HTTP port
- **7082** (HTTPS): Non-conflicting HTTPS port for gateway

---

## Troubleshooting

### Port Already in Use

**Error:** `System.Net.Sockets.SocketException: Only one usage of each socket address is normally permitted`

**Solution:**

1. **Check what's using the port:**
   ```bash
   # Windows
   netstat -ano | findstr :<PORT>

   # Linux/macOS
   lsof -i :<PORT>
   ```

2. **Stop the conflicting process:**
   ```bash
   # Windows (replace <PID> with the process ID from netstat)
   taskkill /PID <PID> /F

   # Linux/macOS
   kill -9 <PID>
   ```

3. **Or change the port temporarily:**
   Modify `AppHost.cs` or `docker-compose.yml` to use a different port.

### SSL Certificate Trust Issues

**Error:** `The SSL connection could not be established` or certificate warnings

**Solution (Local Development):**

```bash
# Trust the .NET development certificate
dotnet dev-certs https --trust

# Verify certificate installation
dotnet dev-certs https --check --trust
```

### Cannot Connect to Service

**Checklist:**

1. ✅ Service is running (`docker ps` or check Aspire Dashboard)
2. ✅ Port is correct for your environment (local vs docker)
3. ✅ Protocol matches (HTTP vs HTTPS)
4. ✅ Using correct profile in Admin UI or CLI
5. ✅ Firewall allows connections
6. ✅ Service is healthy (check `/health` endpoint)

---

## Reference

### Quick Service URL Reference Card

**Local Development (Aspire):**
```
https://localhost:7110  - Tenant (Auth)
https://localhost:7000  - Blueprint
https://localhost:7001  - Wallet
https://localhost:7290  - Register
https://localhost:7002  - Peer
https://localhost:7082  - API Gateway
https://localhost:7083  - Admin UI
http://localhost:18888  - Aspire Dashboard
```

**Docker Compose:**
```
http://localhost:8080/tenant    - Tenant (via Gateway)
http://localhost:8080/blueprint - Blueprint (via Gateway)
http://localhost:8080/wallet    - Wallet (via Gateway)
http://localhost:8080/register  - Register (via Gateway)
http://localhost:8080/peer      - Peer (via Gateway)
http://localhost:18888          - Aspire Dashboard
```

**Production:**
```
https://tenant.sorcha.io    - Tenant (Auth)
https://blueprint.sorcha.io - Blueprint
https://wallet.sorcha.io    - Wallet
https://register.sorcha.io  - Register
https://peer.sorcha.io      - Peer
```

---

## Change History

| Date | Version | Changes |
|------|---------|---------|
| 2025-12-15 | 1.0 | Initial standardized port configuration. Consolidated from 6 profiles to 3 environments. |

---

## See Also

- [Development Status](./development-status.md) - Current implementation status
- [Architecture Overview](./architecture.md) - System architecture diagrams
- [API Documentation](./API-DOCUMENTATION.md) - API endpoint reference
- [README](../README.md) - Project overview and getting started
