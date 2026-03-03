# Azure Database Initialization Guide

**Version:** 1.0
**Last Updated:** 2025-12-23
**Status:** Production Ready

---

## Overview

This document explains how database initialization works in Azure Container Apps deployment vs local Docker, and provides step-by-step instructions for deploying Sorcha to Azure with proper database setup.

---

## Database Architecture

### Services and Databases

| Service | Database | Technology | Purpose |
|---------|----------|------------|---------|
| **Wallet Service** | `sorcha_wallet` | PostgreSQL 17 | Wallet persistence, transactions |
| **Tenant Service** | `sorcha_tenant` | PostgreSQL 17 | Organizations, users, auth |
| **Register Service** | `sorcha_system_register` | Cosmos DB (MongoDB API) | Transaction ledger |
| **Peer Service** | `sorcha_system_register` | Cosmos DB (MongoDB API) | P2P node registry |

---

## Local Docker vs Azure Deployment

### Local Docker (docker-compose.yml)

```yaml
# Local approach uses PostgreSQL container initialization scripts
postgres:
  image: postgres:17-alpine
  volumes:
    - ./docker/postgres-init.sql:/docker-entrypoint-initdb.d/01-init.sql:ro
```

**How it works:**
1. PostgreSQL container starts
2. Initialization script (`postgres-init.sql`) runs **once** when volume is empty
3. Script creates `sorcha_wallet` and `sorcha_tenant` databases
4. Services start and EF Core migrations run automatically via `DatabaseInitializer` HostedService

**Limitations:**
- Only runs on first startup (when volume is empty)
- Requires volume deletion to re-run initialization
- Not applicable to Azure managed databases

### Azure Container Apps Deployment

```bicep
// Azure approach uses Bicep templates to create databases
resource walletDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2023-12-01-preview' = {
  parent: postgresServer
  name: 'sorcha_wallet'
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}
```

**How it works:**
1. **Infrastructure as Code (Bicep)** creates:
   - Azure Database for PostgreSQL Flexible Server
   - Databases: `sorcha_wallet`, `sorcha_tenant`
   - Azure Cosmos DB with MongoDB API
   - Database: `sorcha_system_register`
2. **Container Apps** deploy with database connection strings
3. **EF Core migrations** run automatically on service startup
4. **Tenant Service** seeds default organization and service principals

---

## Azure Database Resources

### PostgreSQL Flexible Server

**Configuration:**
- **SKU:** Standard_B1ms (Burstable) - 1 vCore, 2GB RAM
- **Version:** PostgreSQL 17
- **Storage:** 32GB (with auto-grow enabled)
- **Backup:** 7-day retention, no geo-redundancy
- **Cost:** ~$12/month (Burstable tier)

**Databases Created:**
- `sorcha_wallet` - Wallet Service data
- `sorcha_tenant` - Tenant/Auth Service data

**Firewall Rules:**
- Allows access from Azure services (Container Apps)
- SSL/TLS required for connections

### Cosmos DB (MongoDB API)

**Configuration:**
- **Tier:** Serverless (pay-per-request)
- **API:** MongoDB 6.0 compatibility
- **Consistency:** Session level
- **Cost:** Pay-per-request (~$0.25/million operations)

**Database Created:**
- `sorcha_system_register` - Transaction ledger

**Collections:**
- `transactions` - Sharded by transaction ID

---

## Automatic Migration Process

### EF Core Migration Flow

Both Wallet and Tenant services use **automatic migrations on startup**:

#### Wallet Service

```csharp
// src/Services/Sorcha.Wallet.Service/Program.cs
await app.Services.ApplyWalletDatabaseMigrationsAsync();
```

**What happens:**
1. Service starts
2. Checks if `WalletDbContext` is configured
3. Runs `context.Database.MigrateAsync()`
4. Creates tables if they don't exist
5. Applies any pending migrations

#### Tenant Service

```csharp
// src/Services/Sorcha.Tenant.Service/Extensions/ServiceCollectionExtensions.cs
builder.Services.AddDatabaseInitializer();
```

**What happens:**
1. `DatabaseInitializerHostedService` runs on startup
2. Runs `context.Database.MigrateAsync()`
3. Seeds default organization (`sorcha.local`)
4. Creates default admin user (`admin@sorcha.local`)
5. Creates service principals for all services

**Default Credentials (Change in Production!):**
- Email: `admin@sorcha.local`
- Password: `Dev_Pass_2025!`

---

## Deployment Instructions

### Prerequisites

1. **Azure Subscription** with permissions to create resources
2. **Azure CLI** installed and authenticated
3. **Docker** installed for building container images
4. **PostgreSQL password** for admin account

### Step 1: Set Deployment Parameters

```bash
# Set environment variables
export RESOURCE_GROUP="sorcha-prod-rg"
export LOCATION="eastus"
export CONTAINER_REGISTRY="sorchacr"
export ENVIRONMENT="prod"

# Generate secure PostgreSQL password
export POSTGRES_PASSWORD=$(openssl rand -base64 32)
echo "PostgreSQL Password: $POSTGRES_PASSWORD" # SAVE THIS!
```

### Step 2: Deploy Infrastructure

```bash
# Deploy all infrastructure (databases + container apps)
az deployment sub create \
  --location $LOCATION \
  --template-file infra/main.bicep \
  --parameters \
    resourceGroupName=$RESOURCE_GROUP \
    location=$LOCATION \
    containerRegistryName=$CONTAINER_REGISTRY \
    environment=$ENVIRONMENT \
    postgresAdminPassword=$POSTGRES_PASSWORD
```

**What this creates:**
- Resource Group
- Azure Database for PostgreSQL Flexible Server
  - `sorcha_wallet` database
  - `sorcha_tenant` database
- Azure Cosmos DB (MongoDB API)
  - `sorcha_system_register` database
- Azure Cache for Redis
- Azure Container Registry
- Container Apps Environment
- All Container Apps (but not started yet - no images)

### Step 3: Build and Push Container Images

```bash
# Login to Azure Container Registry
az acr login --name $CONTAINER_REGISTRY

# Build and push all service images
docker-compose build
docker tag sorcha/wallet-service:latest $CONTAINER_REGISTRY.azurecr.io/wallet-service:latest
docker tag sorcha/tenant-service:latest $CONTAINER_REGISTRY.azurecr.io/tenant-service:latest
docker tag sorcha/blueprint-service:latest $CONTAINER_REGISTRY.azurecr.io/blueprint-api:latest
docker tag sorcha/peer-service:latest $CONTAINER_REGISTRY.azurecr.io/peer-service:latest
docker tag sorcha/api-gateway:latest $CONTAINER_REGISTRY.azurecr.io/api-gateway:latest

docker push $CONTAINER_REGISTRY.azurecr.io/wallet-service:latest
docker push $CONTAINER_REGISTRY.azurecr.io/tenant-service:latest
docker push $CONTAINER_REGISTRY.azurecr.io/blueprint-api:latest
docker push $CONTAINER_REGISTRY.azurecr.io/peer-service:latest
docker push $CONTAINER_REGISTRY.azurecr.io/api-gateway:latest
```

### Step 4: Restart Container Apps

```bash
# Restart all apps to pull new images and run migrations
az containerapp restart --name wallet-service --resource-group $RESOURCE_GROUP
az containerapp restart --name tenant-service --resource-group $RESOURCE_GROUP
az containerapp restart --name blueprint-api --resource-group $RESOURCE_GROUP
az containerapp restart --name peer-service --resource-group $RESOURCE_GROUP
az containerapp restart --name api-gateway --resource-group $RESOURCE_GROUP
```

### Step 5: Verify Database Initialization

```bash
# Check Tenant Service logs for database initialization
az containerapp logs show \
  --name tenant-service \
  --resource-group $RESOURCE_GROUP \
  --tail 100 \
  --follow
```

**Look for:**
```
[INF] Applying 2 pending migration(s): 20251210174739_InitialCreate, 20251223153922_UpdateBrandingToJson
[INF] Database migrations applied successfully
[INF] Creating default organization: Sorcha Local
[INF] Default organization created with ID: 00000000-0000-0000-0000-000000000001
[INF] Creating default administrator: admin@sorcha.local
[INF] Default administrator created with ID: 00000000-0000-0000-0001-000000000001
[WRN] Default admin credentials - Email: admin@sorcha.local, Password: Dev_Pass_2025!
[INF] Creating service principal: Blueprint Service
[INF] Creating service principal: Wallet Service
[INF] Creating service principal: Register Service
[INF] Creating service principal: Peer Service
[INF] Database initialization completed successfully
```

**Save the service principal credentials shown in the logs!**

```bash
# Check Wallet Service logs
az containerapp logs show \
  --name wallet-service \
  --resource-group $RESOURCE_GROUP \
  --tail 50
```

**Look for:**
```
Applying Wallet Service database migrations...
Wallet Service database migrations applied successfully
```

### Step 6: Get Application URLs

```bash
# Get API Gateway URL
az containerapp show \
  --name api-gateway \
  --resource-group $RESOURCE_GROUP \
  --query properties.configuration.ingress.fqdn \
  --output tsv
```

---

## Database Connection Management

### Connection Strings in Azure

Connection strings are stored as **Container App secrets** and passed as environment variables:

```bicep
secrets: [
  {
    name: 'wallet-db-connection'
    value: 'Host=sorcha-postgres-prod.postgres.database.azure.com;Database=sorcha_wallet;Username=sorcha_admin;Password=***;SslMode=Require'
  }
]

env: [
  {
    name: 'ConnectionStrings__wallet-db'
    secretRef: 'wallet-db-connection'
  }
]
```

### Best Practices

**Development/Staging:**
- Use Bicep template outputs for connection strings
- Store in Container App secrets

**Production:**
- Use **Azure Key Vault** for connection strings
- Enable **Managed Identity** for Container Apps
- Reference Key Vault secrets in Container Apps:

```bicep
// Future improvement: Key Vault integration
secrets: [
  {
    name: 'wallet-db-connection'
    keyVaultUrl: 'https://sorcha-kv.vault.azure.net/secrets/wallet-db-connection'
    identity: 'system'
  }
]
```

---

## Database Maintenance

### Applying New Migrations

**Scenario:** You've created a new EF Core migration locally and need to apply it in Azure.

**Steps:**
1. **Create migration locally:**
   ```bash
   dotnet ef migrations add YourMigrationName --context WalletDbContext
   ```

2. **Build and push new image:**
   ```bash
   docker-compose build wallet-service
   docker tag sorcha/wallet-service:latest $CONTAINER_REGISTRY.azurecr.io/wallet-service:latest
   docker push $CONTAINER_REGISTRY.azurecr.io/wallet-service:latest
   ```

3. **Restart Container App:**
   ```bash
   az containerapp restart --name wallet-service --resource-group $RESOURCE_GROUP
   ```

4. **Verify migration applied:**
   ```bash
   az containerapp logs show --name wallet-service --resource-group $RESOURCE_GROUP --tail 50
   ```

**The migration runs automatically on startup** - no manual database access required!

### Manual Database Access

**Connect to Azure PostgreSQL:**

```bash
# Get PostgreSQL FQDN
POSTGRES_FQDN=$(az postgres flexible-server show \
  --resource-group $RESOURCE_GROUP \
  --name sorcha-postgres-prod \
  --query fullyQualifiedDomainName \
  --output tsv)

# Connect with psql
psql "host=$POSTGRES_FQDN port=5432 dbname=sorcha_wallet user=sorcha_admin password=$POSTGRES_PASSWORD sslmode=require"
```

**Connect to Cosmos DB (MongoDB):**

```bash
# Get MongoDB connection string
MONGO_CONN=$(az cosmosdb keys list \
  --name sorcha-cosmos-prod \
  --resource-group $RESOURCE_GROUP \
  --type connection-strings \
  --query "connectionStrings[0].connectionString" \
  --output tsv)

# Connect with mongosh
mongosh "$MONGO_CONN"
```

---

## Troubleshooting

### Problem: Migrations Not Running

**Symptoms:**
- Service starts but tables don't exist
- Errors about missing tables in logs

**Solution:**
```bash
# Check if database connection is configured
az containerapp logs show --name wallet-service --resource-group $RESOURCE_GROUP | grep "migration"

# Verify connection string secret exists
az containerapp secret list --name wallet-service --resource-group $RESOURCE_GROUP

# Restart to force migration retry
az containerapp restart --name wallet-service --resource-group $RESOURCE_GROUP
```

### Problem: Connection Timeout

**Symptoms:**
```
Npgsql.NpgsqlException: Timeout during connection attempt
```

**Solution:**
```bash
# Verify PostgreSQL firewall rules
az postgres flexible-server firewall-rule list \
  --resource-group $RESOURCE_GROUP \
  --name sorcha-postgres-prod

# Add rule to allow Azure services (should already exist)
az postgres flexible-server firewall-rule create \
  --resource-group $RESOURCE_GROUP \
  --name sorcha-postgres-prod \
  --rule-name AllowAzureServices \
  --start-ip-address 0.0.0.0 \
  --end-ip-address 0.0.0.0
```

### Problem: Authentication Failed

**Symptoms:**
```
password authentication failed for user "sorcha_admin"
```

**Solution:**
```bash
# Update PostgreSQL admin password
az postgres flexible-server update \
  --resource-group $RESOURCE_GROUP \
  --name sorcha-postgres-prod \
  --admin-password $NEW_PASSWORD

# Update Container App secret
az containerapp secret set \
  --name wallet-service \
  --resource-group $RESOURCE_GROUP \
  --secrets wallet-db-connection="Host=...;Password=$NEW_PASSWORD;..."

# Restart service
az containerapp restart --name wallet-service --resource-group $RESOURCE_GROUP
```

### Problem: Tenant Service Not Seeding Data

**Symptoms:**
- No default organization created
- Can't login with default credentials

**Solution:**
```bash
# Check if database is empty
az containerapp exec \
  --name tenant-service \
  --resource-group $RESOURCE_GROUP \
  --command "dotnet ef database update"

# Delete and recreate database (CAUTION: deletes all data!)
az postgres flexible-server db delete \
  --resource-group $RESOURCE_GROUP \
  --server-name sorcha-postgres-prod \
  --database-name sorcha_tenant

az postgres flexible-server db create \
  --resource-group $RESOURCE_GROUP \
  --server-name sorcha-postgres-prod \
  --database-name sorcha_tenant

# Restart to trigger re-initialization
az containerapp restart --name tenant-service --resource-group $RESOURCE_GROUP
```

---

## Cost Optimization

### Database Costs

| Resource | Configuration | Monthly Cost (Estimate) |
|----------|---------------|-------------------------|
| PostgreSQL Flexible Server | Standard_B1ms (Burstable) | ~$12 |
| Cosmos DB | Serverless (low usage) | ~$1-5 |
| Redis Cache | Basic C0 (250MB) | ~$16 |
| Container Apps | Consumption (100 requests/day) | ~$5 |
| Container Registry | Basic | ~$5 |
| **Total** | | **~$39-43/month** |

### Cost Saving Tips

1. **Use Free Tier for Development:**
   ```bicep
   enableFreeTier: true // One free Cosmos DB per subscription
   ```

2. **Stop non-production resources:**
   ```bash
   # Stop PostgreSQL when not in use
   az postgres flexible-server stop \
     --resource-group $RESOURCE_GROUP \
     --name sorcha-postgres-prod
   ```

3. **Scale down Container Apps:**
   ```bicep
   minReplicas: 0 // Scale to zero when idle
   ```

4. **Use reserved capacity for production:**
   - PostgreSQL Reserved Capacity: Save up to 55%
   - Cosmos DB Reserved Capacity: Save up to 65%

---

## Security Considerations

### Secrets Management

**Current Implementation (Development):**
- Connection strings stored in Container App secrets
- Passwords passed via Bicep parameters

**Production Recommendations:**
1. **Use Azure Key Vault:**
   - Store all connection strings in Key Vault
   - Use Managed Identity for Container Apps
   - Reference secrets via Key Vault URIs

2. **Rotate Credentials Regularly:**
   ```bash
   # Rotate PostgreSQL password quarterly
   az postgres flexible-server update \
     --resource-group $RESOURCE_GROUP \
     --name sorcha-postgres-prod \
     --admin-password $(openssl rand -base64 32)
   ```

3. **Enable Advanced Threat Protection:**
   ```bash
   az postgres flexible-server update \
     --resource-group $RESOURCE_GROUP \
     --name sorcha-postgres-prod \
     --threat-detection-enabled true
   ```

4. **Audit Database Access:**
   - Enable PostgreSQL audit logging
   - Send logs to Log Analytics workspace
   - Set up alerts for suspicious activity

### Network Security

**Current Implementation:**
- Public endpoint with firewall rules
- SSL/TLS required for all connections

**Production Recommendations:**
1. **Use Private Endpoints:**
   ```bicep
   publicNetworkAccess: 'Disabled'
   ```

2. **VNet Integration:**
   - Deploy Container Apps in VNet
   - Use Private Link for PostgreSQL
   - Use Service Endpoints for Cosmos DB

---

## Summary: Local vs Azure

| Aspect | Local Docker | Azure Container Apps |
|--------|--------------|----------------------|
| **Database Creation** | PostgreSQL init script | Bicep infrastructure template |
| **Migrations** | EF Core auto-migrate on startup | EF Core auto-migrate on startup |
| **Seed Data** | HostedService (DatabaseInitializer) | HostedService (DatabaseInitializer) |
| **Connection Strings** | docker-compose environment vars | Container App secrets |
| **Cost** | $0 (local resources) | ~$40/month (Azure resources) |
| **Backup** | Manual (volume snapshots) | Automated (7-day retention) |
| **Scaling** | Manual container restart | Auto-scale (0-5 replicas) |
| **High Availability** | Single instance | Multi-zone (if enabled) |

**Key Takeaway:** The database initialization **logic is identical** in both environments - it's just the infrastructure provisioning that differs (Docker init script vs Bicep templates).

---

## Next Steps

1. ✅ Review this documentation
2. ✅ Prepare Azure subscription and credentials
3. ✅ Run deployment steps (Step 1-6 above)
4. ✅ Verify database initialization in logs
5. ✅ Save service principal credentials
6. ✅ Change default admin password
7. ✅ Configure production secrets in Key Vault
8. ✅ Set up monitoring and alerts

---

**Questions or Issues?**
See [TROUBLESHOOTING.md](../TROUBLESHOOTING.md) or create a GitHub issue.
