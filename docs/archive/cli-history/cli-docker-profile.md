# Using the Sorcha CLI with Docker

This guide explains how to use the Sorcha CLI with Docker-based services.

## Docker Profile

The CLI includes a preconfigured `docker` profile that routes all API calls through the API Gateway running on `localhost:8080`.

### Profile Configuration

The `docker` profile uses the following endpoints:

| Service | URL |
|---------|-----|
| **Tenant Service** | `http://localhost:8080/tenant` |
| **Register Service** | `http://localhost:8080/register` |
| **Peer Service** | `http://localhost:8080/peer` |
| **Wallet Service** | `http://localhost:8080/wallet` |
| **Auth Token Endpoint** | `http://localhost:8080/tenant/api/service-auth/token` |

### Starting Docker Services

Before using the CLI with the docker profile, ensure the services are running:

```bash
# Start all services with docker-compose
docker-compose up -d

# Verify services are running
docker-compose ps

# Check API Gateway logs
docker-compose logs -f apigateway
```

### Using the Docker Profile

#### Method 1: Per-Command Profile Flag

Use the `--profile docker` flag with any command:

```bash
# Authenticate
sorcha auth login --profile docker

# List organizations
sorcha org list --profile docker

# Create a wallet
sorcha wallet create --name MyWallet --algorithm ED25519 --profile docker

# Check peer health
sorcha peer health --profile docker
```

#### Method 2: Set as Active Profile

Set docker as your active profile to avoid repeating the flag:

```bash
# Set docker as active profile (requires auth login first)
sorcha auth login --profile docker

# Now all commands use the docker profile by default
sorcha org list
sorcha wallet list
sorcha peer stats
```

### Authentication with Docker Profile

The docker profile uses the same authentication flow as other profiles:

```bash
# Login with service principal
sorcha auth login --profile docker \
  --client-id your-client-id \
  --client-secret your-client-secret

# Check authentication status
sorcha auth status --profile docker

# Logout when done
sorcha auth logout --profile docker
```

### Troubleshooting

#### Services Not Responding

If commands fail with connection errors:

1. **Check services are running:**
   ```bash
   docker-compose ps
   ```

2. **Verify API Gateway is listening on port 8080:**
   ```bash
   curl http://localhost:8080/health
   ```

3. **Check service logs:**
   ```bash
   docker-compose logs apigateway
   docker-compose logs tenant-service
   ```

#### Authentication Issues

If authentication fails:

1. **Ensure Tenant Service is healthy:**
   ```bash
   curl http://localhost:8080/tenant/health
   ```

2. **Verify your credentials are correct**

3. **Check token endpoint is accessible:**
   ```bash
   curl -X POST http://localhost:8080/tenant/api/service-auth/token \
     -H "Content-Type: application/json" \
     -d '{"clientId":"your-id","clientSecret":"your-secret"}'
   ```

### Switching Between Profiles

You can easily switch between different environments:

```bash
# Use dev profile (direct HTTPS to services)
sorcha auth login --profile dev

# Use docker profile (through API Gateway)
sorcha auth login --profile docker

# Use local profile (direct HTTP to services)
sorcha auth login --profile local

# Check current profile status
sorcha auth status
```

### Available Profiles

| Profile | Use Case | URLs |
|---------|----------|------|
| **dev** | Development with HTTPS | `https://localhost:7080-7083` |
| **local** | Local development with HTTP | `http://localhost:5080-5083` |
| **docker** | Docker containers via API Gateway | `http://localhost:8080/*` |
| **staging** | Staging environment | `https://staging-*.sorcha.io` |
| **production** | Production environment | `https://*.sorcha.io` |

### Example Workflow

Complete workflow using the docker profile:

```bash
# 1. Start Docker services
docker-compose up -d

# 2. Wait for services to be healthy
sleep 10

# 3. Authenticate
sorcha auth login --profile docker \
  --client-id cli-admin \
  --client-secret your-secret

# 4. Create an organization
sorcha org create --name "Acme Corp" --profile docker

# 5. Create a user
sorcha user create \
  --org-id org-123 \
  --email admin@acme.com \
  --profile docker

# 6. Create a wallet
sorcha wallet create \
  --name "Acme Wallet" \
  --algorithm ED25519 \
  --profile docker

# 7. Create a register
sorcha register create \
  --name "Acme Ledger" \
  --org-id org-123 \
  --profile docker

# 8. Check peer network health
sorcha peer health --profile docker

# 9. View network statistics
sorcha peer stats --profile docker
```

### API Gateway Routes

The API Gateway routes requests to backend services:

```
http://localhost:8080/tenant/*    → Tenant Service (port 5110)
http://localhost:8080/register/*  → Register Service (port 5290)
http://localhost:8080/peer/*      → Peer Service (port 5002)
http://localhost:8080/wallet/*    → Wallet Service (port 5001)
```

### Configuration File Location

Profile configurations are stored in:
- **Windows:** `%USERPROFILE%\.sorcha\config.json`
- **Linux/macOS:** `~/.sorcha/config.json`

You can manually edit this file to customize the docker profile URLs if needed.

### Next Steps

- See [CLI Documentation](./CLI-DOCUMENTATION.md) for all available commands
- See [Docker Setup](../docker-compose.yml) for service configuration
- See [API Gateway Configuration](../src/Apps/Sorcha.ApiGateway/) for routing details
