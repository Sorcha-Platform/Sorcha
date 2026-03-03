# Docker Development Workflow

**Status:** Active Development Profile
**Last Updated:** 2026-01-05

## Overview

Sorcha now uses **docker-compose as the primary development environment**. When you modify services or apps, Docker containers must be rebuilt and restarted.

## Quick Reference

### Start All Services

```bash
docker-compose up -d
```

### Stop All Services

```bash
docker-compose down
```

### Rebuild & Restart a Single Service

```bash
# After modifying service code
docker-compose build <service-name>
docker-compose up -d --force-recreate <service-name>

# Example: After changing Register Service
docker-compose build register-service
docker-compose up -d --force-recreate register-service
```

### Rebuild & Restart All Services

```bash
docker-compose build
docker-compose up -d --force-recreate
```

### View Logs

```bash
# All services
docker-compose logs -f

# Specific service
docker-compose logs -f register-service

# Last 50 lines
docker logs sorcha-register-service --tail 50
```

## Service Names

| Service | Container Name | Build Required When |
|---------|----------------|---------------------|
| `register-service` | sorcha-register-service | Register Service code changes |
| `validator-service` | sorcha-validator-service | Validator Service code changes |
| `wallet-service` | sorcha-wallet-service | Wallet Service code changes |
| `tenant-service` | sorcha-tenant-service | Tenant Service code changes |
| `blueprint-service` | sorcha-blueprint-service | Blueprint Service code changes |
| `peer-service` | sorcha-peer-service | Peer Service code changes |
| `api-gateway` | sorcha-api-gateway | API Gateway code changes |
| `admin-ui` | sorcha-admin | Admin UI code changes |

## Common Development Workflows

### 1. Making Code Changes

**After modifying a service:**

```bash
# 1. Build the updated service
dotnet build src/Services/Sorcha.Register.Service

# 2. Rebuild Docker image
docker-compose build register-service

# 3. Restart the container
docker-compose up -d --force-recreate register-service

# 4. Check logs for errors
docker logs sorcha-register-service --tail 20
```

### 2. Testing Changes

```bash
# Check service is running
docker ps --filter "name=sorcha-register-service"

# Test endpoint
curl http://localhost:5290/health

# Run walkthrough tests
pwsh walkthroughs/RegisterCreationFlow/test-register-creation-docker.ps1
```

### 3. Fixing DI/Configuration Issues

**Common issues and solutions:**

```bash
# Issue: "Cannot consume scoped service from singleton"
# Fix: Change service lifetime in Program.cs, then:
docker-compose build <service-name>
docker-compose up -d --force-recreate <service-name>

# Issue: "Service address not configured"
# Fix: Add ServiceClients config to docker-compose.yml, then:
docker-compose up -d --force-recreate <service-name>

# Issue: Container keeps restarting
# Check logs:
docker logs sorcha-<service-name> --tail 100
```

### 4. Clean Rebuild

**When things get messy:**

```bash
# Stop everything
docker-compose down

# Remove all containers and networks
docker-compose down --remove-orphans

# Rebuild all images from scratch
docker-compose build --no-cache

# Start fresh
docker-compose up -d

# Check everything is healthy
docker-compose ps
```

## Port Mappings

### Exposed Services (Localhost Access)

| Service | Internal Port | Localhost Port | Purpose |
|---------|--------------|----------------|---------|
| Register Service | 8080 | 5290 | Walkthrough testing |
| Validator Service | 8080 | 5100 | Walkthrough testing |
| API Gateway | 8080 | 5110 | External API access |
| Admin UI | 80 | 5111 | Web interface |
| Aspire Dashboard | 18888 | 18888 | Service monitoring |

### Internal Services (Docker Network Only)

- Wallet Service: `http://sorcha-wallet-service:8080`
- Tenant Service: `http://sorcha-tenant-service:8080`
- Blueprint Service: `http://sorcha-blueprint-service:8080`
- Peer Service: `http://sorcha-peer-service:8080`
- Peer Hub: `http://sorcha-peer-hub-local:8080`

### Infrastructure

- PostgreSQL: `localhost:5432`
- MongoDB: `localhost:27017`
- Redis: `localhost:6379`

## Environment Configuration

### Service Client Configuration Pattern

All services that call other services need ServiceClients configuration:

```yaml
environment:
  ServiceClients__WalletService__Address: http://sorcha-wallet-service:8080
  ServiceClients__WalletService__UseGrpc: "false"
  ServiceClients__ValidatorService__Address: http://sorcha-validator-service:8080
  ServiceClients__ValidatorService__UseGrpc: "false"
```

**Services requiring ServiceClients:**
- Register Service → Wallet, Validator
- Validator Service → Wallet, Register, Peer, Blueprint
- API Gateway → All services
- Blueprint Service → Register
- Peer Service → Register

### JWT Configuration

All services share the same JWT signing key:

```yaml
JwtSettings__SigningKey: ${JWT_SIGNING_KEY:-your-development-key-here}
JwtSettings__Issuer: "http://localhost"
JwtSettings__Audience: "https://sorcha.local"
```

## Development vs AppHost

| Aspect | Docker-Compose | .NET Aspire AppHost |
|--------|----------------|---------------------|
| **Primary Use** | Active Development | Local testing/debugging |
| **Port Control** | docker-compose.yml | AppHost.cs + launchSettings.json |
| **Service Discovery** | Docker DNS | .NET Aspire |
| **Code Changes** | Rebuild container | Restart AppHost |
| **Debugging** | Docker logs | Visual Studio debugger |
| **Speed** | Slower rebuild | Faster startup |
| **Isolation** | Complete | Shared host |

**When to use Docker-Compose:**
- ✅ Active feature development
- ✅ Testing service integration
- ✅ Running walkthroughs
- ✅ Multi-service testing
- ✅ Production-like environment

**When to use AppHost:**
- Local debugging with breakpoints
- Quick single-service testing
- IDE integration testing
- Aspire dashboard features

## Troubleshooting

### Container Won't Start

```bash
# Check container status
docker ps -a --filter "name=sorcha-register-service"

# View crash logs
docker logs sorcha-register-service --tail 100

# Common issues:
# - DI lifetime mismatch → Check Program.cs service registrations
# - Missing configuration → Check docker-compose.yml environment
# - Port conflict → Check port mappings
# - Database not ready → Check depends_on healthchecks
```

### Service Returns 401/403

```bash
# Check if endpoint requires authorization
# Fix: Use .AllowAnonymous() or configure JWT properly

# Check logs for auth failures
docker logs sorcha-register-service | grep -i "auth\|401\|403"
```

### Service Returns 500

```bash
# Check for unhandled exceptions
docker logs sorcha-register-service --tail 50 | grep -i "exception\|error"

# Common causes:
# - Missing ServiceClients configuration
# - Invalid connection strings
# - Dependency service not available
```

### Network Issues

```bash
# Verify Docker network exists
docker network ls | grep sorcha

# Check DNS resolution
docker run --rm --network sorcha_sorcha-network alpine/curl:latest \
  curl -s http://sorcha-register-service:8080/health

# Recreate network
docker-compose down
docker-compose up -d
```

## Best Practices

1. **Always rebuild after code changes:**
   ```bash
   docker-compose build <service-name>
   docker-compose up -d --force-recreate <service-name>
   ```

2. **Check logs immediately after restart:**
   ```bash
   docker logs sorcha-<service-name> --tail 20
   ```

3. **Test endpoints after changes:**
   ```bash
   curl http://localhost:5290/health
   ```

4. **Use walkthroughs for integration testing:**
   ```bash
   pwsh walkthroughs/RegisterCreationFlow/test-register-creation-docker.ps1
   ```

5. **Keep docker-compose.yml in sync with code dependencies**

6. **Document configuration changes in commit messages**

## Helper Scripts

### Quick Rebuild Script

Create `scripts/rebuild-service.sh`:

```bash
#!/bin/bash
SERVICE=$1

if [ -z "$SERVICE" ]; then
    echo "Usage: ./rebuild-service.sh <service-name>"
    exit 1
fi

echo "Rebuilding $SERVICE..."
docker-compose build $SERVICE
echo "Restarting $SERVICE..."
docker-compose up -d --force-recreate $SERVICE
echo "Checking logs..."
docker logs sorcha-$SERVICE --tail 20
```

### Quick Test Script

Create `scripts/test-service.sh`:

```bash
#!/bin/bash
SERVICE=$1
PORT=$2

if [ -z "$SERVICE" ] || [ -z "$PORT" ]; then
    echo "Usage: ./test-service.sh <service-name> <port>"
    exit 1
fi

echo "Testing $SERVICE on port $PORT..."
curl -s http://localhost:$PORT/health | jq
```

## Additional Resources

- Docker Compose Documentation: https://docs.docker.com/compose/
- .NET Docker Best Practices: https://learn.microsoft.com/dotnet/core/docker/
- Sorcha Architecture: [docs/architecture.md](../reference/architecture.md)
- Walkthrough Testing: [walkthroughs/README.md](../walkthroughs/README.md)

---

**Remember:** Docker-compose is now the primary development environment. Always rebuild containers after code changes!
