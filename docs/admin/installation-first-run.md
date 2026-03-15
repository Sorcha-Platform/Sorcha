# Installation & First Run

Step-by-step guide to deploy Sorcha using Docker Compose.

## Installation Flow

```
┌─────────────┐     ┌─────────────┐     ┌──────────────┐
│   Clone &   │────>│  Configure  │────>│   docker     │
│   Install   │     │    .env     │     │ compose up   │
└─────────────┘     └─────────────┘     └──────┬───────┘
                                               │
                                        ┌──────v───────┐
                                        │  Bootstrap   │
                                        │  & Verify    │
                                        └──────────────┘
```

## Step 1: Clone the Repository

```bash
git clone https://github.com/your-org/sorcha.git
cd sorcha
```

## Step 2: Configure Environment

Copy the example environment file and customize it:

```bash
cp .env.example .env
```

Edit `.env` with your preferred editor. Key variables to set:

| Variable | Purpose | Action |
|----------|---------|--------|
| `INSTALLATION_NAME` | JWT issuer identity | Set to your domain (e.g., `sorcha.example.com`) |
| `JWT_SIGNING_KEY` | Token signing key | Generate a new 256-bit key (see below) |
| `POSTGRES_PASSWORD` | PostgreSQL password | Change from default |
| `MONGO_PASSWORD` | MongoDB password | Change from default |
| `OPENAPI_REQUIRE_AUTH` | Lock down API docs | Set `true` for production |

### Generate a JWT Signing Key

The JWT signing key must be a Base64-encoded 256-bit (32-byte) value:

```bash
# Linux/macOS
openssl rand -base64 32

# PowerShell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }) -as [byte[]])
```

Copy the output into `JWT_SIGNING_KEY` in your `.env` file.

**Important:** All services must share the same `JWT_SIGNING_KEY` and `INSTALLATION_NAME`. These are distributed automatically via the `x-jwt-env` anchor in `docker-compose.yml`.

## Step 3: Start Services

```bash
docker-compose up -d
```

Expected startup sequence (services start in dependency order):

1. **Infrastructure** (Redis, PostgreSQL, MongoDB, Aspire Dashboard) -- ~10s
2. **Wallet Service** -- depends on Redis, PostgreSQL
3. **Tenant Service** -- depends on Redis, PostgreSQL, Wallet
4. **Register Service** -- depends on Redis, MongoDB, Wallet, Tenant
5. **Validator Service** -- depends on Redis, MongoDB, Wallet, Tenant
6. **Peer Service** -- depends on Redis, MongoDB
7. **Blueprint Service** -- depends on all core services
8. **UI Web** -- depends on Aspire Dashboard
9. **API Gateway** -- depends on all services (starts last)

Full startup typically takes 60-90 seconds. Monitor progress:

```bash
# Watch container status
docker-compose ps

# Follow startup logs
docker-compose logs -f

# Watch a specific service
docker-compose logs -f api-gateway
```

## Step 4: Verify Health Checks

Wait for all services to report healthy, then verify:

```bash
# API Gateway health
curl http://localhost/health

# Individual service health (via gateway)
curl http://localhost/blueprint/health
curl http://localhost/tenant/health
curl http://localhost/register/health
curl http://localhost/wallet/health
curl http://localhost/validator/health
curl http://localhost/peer/health
```

Each endpoint should return HTTP 200 with status `Healthy`.

### Check All Containers

```bash
docker-compose ps
```

All containers should show `Up` with `(healthy)` status. If any container is in a restart loop, check its logs:

```bash
docker-compose logs <service-name>
```

## Step 5: Bootstrap -- First Admin Account

On first startup, the Tenant Service automatically creates:

- A default organization: **Sorcha Local** (`sorcha-local`)
- A default admin user: `admin@sorcha.local` / `Dev_Pass_2025!`
- Service principal accounts for inter-service authentication

### Log In

```bash
curl -X POST http://localhost/tenant/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "admin@sorcha.local",
    "password": "Dev_Pass_2025!"
  }'
```

The response includes a JWT `access_token`. Save this for subsequent API calls.

### Change the Default Password

**Important:** Change the default admin password immediately after first login.

```bash
curl -X POST http://localhost/tenant/api/auth/change-password \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <your-token>" \
  -d '{
    "currentPassword": "Dev_Pass_2025!",
    "newPassword": "YourSecurePassword123!"
  }'
```

## Step 6: Fix Wallet Encryption Permissions

On fresh installations, the wallet encryption key volume may have incorrect ownership. Fix this before creating wallets:

```bash
# Linux/macOS
./scripts/fix-wallet-encryption-permissions.sh

# Windows (PowerShell)
./scripts/fix-wallet-encryption-permissions.ps1

# Or manually
docker run --rm -v sorcha_wallet-encryption-keys:/data alpine chown -R 1654:1654 /data
```

## Step 7: Verification Checklist

Confirm the installation is working:

- [ ] All containers are running and healthy: `docker-compose ps`
- [ ] API Gateway responds: `curl http://localhost/health`
- [ ] OpenAPI documentation loads: open `http://localhost/openapi` in a browser
- [ ] Admin login succeeds (see Step 5)
- [ ] Aspire Dashboard is accessible: `http://localhost:18888`
- [ ] UI loads: `http://localhost/app`

## System Register

The System Register provides a shared ledger for platform-level governance data (blueprints, policies). It bootstraps automatically on first startup -- no environment variable is needed. The register service creates the well-known system register and seeds default blueprints idempotently.

To verify bootstrap succeeded, check the register service logs or query:
```bash
curl http://localhost:5380/api/system-register
```

## Optional: Aspire AppHost Mode (Development)

For local development with full debugging support, you can run services directly via .NET Aspire instead of Docker:

```bash
# Requires .NET 10 SDK
dotnet run --project src/Apps/Sorcha.AppHost
```

This starts all services on HTTPS ports (7000-7290) with the Aspire Dashboard at `http://localhost:18888`. See [Port Configuration](../getting-started/PORT-CONFIGURATION.md) for the full port mapping.

## Next Steps

- [Configuration Reference](configuration-reference.md) -- Tune environment variables
- [Monitoring & Observability](monitoring-observability.md) -- Set up dashboards and alerts
- [Administration](administration.md) -- Create organizations and users
- [Security Hardening](administration.md#security-hardening-checklist) -- Production security steps
