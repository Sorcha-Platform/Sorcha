# Azure Deployment - Quick Start Guide

**5-Minute Setup Guide for Azure Container Apps Deployment**

---

## What's Different from Local Docker?

| Aspect | Local Docker | Azure |
|--------|--------------|-------|
| **Database Creation** | Init script in container | Bicep template creates managed databases |
| **Database Type** | PostgreSQL container | Azure Database for PostgreSQL Flexible Server |
| **MongoDB** | MongoDB container | Azure Cosmos DB (MongoDB API) |
| **Migrations** | Auto on startup (same) | Auto on startup (same) |
| **Seed Data** | Auto on startup (same) | Auto on startup (same) |

**Key Point:** The application code is **identical** - only infrastructure provisioning differs!

---

## Quick Deploy (3 Commands)

### Prerequisites
- Azure CLI installed: `az --version`
- Logged in: `az login`
- Docker running locally

### Option 1: PowerShell Script (Recommended)

```powershell
# Deploy everything automatically
.\scripts\deploy-azure.ps1 `
    -ResourceGroupName "sorcha-prod-rg" `
    -Location "eastus" `
    -ContainerRegistryName "sorchacr" `
    -Environment "prod"
```

**That's it!** The script will:
1. ✅ Generate secure PostgreSQL password
2. ✅ Deploy all Azure infrastructure (databases, Container Apps, Redis)
3. ✅ Build and push all Docker images
4. ✅ Restart services to run migrations

### Option 2: Manual Steps

```bash
# 1. Generate password
export POSTGRES_PASSWORD=$(openssl rand -base64 32)
echo "Save this: $POSTGRES_PASSWORD"

# 2. Deploy infrastructure
az deployment sub create \
  --location eastus \
  --template-file infra/main.bicep \
  --parameters \
    resourceGroupName=sorcha-prod-rg \
    containerRegistryName=sorchacr \
    environment=prod \
    postgresAdminPassword=$POSTGRES_PASSWORD

# 3. Build and push images
az acr login --name sorchacr
docker-compose build
docker tag sorcha/wallet-service:latest sorchacr.azurecr.io/wallet-service:latest
docker push sorchacr.azurecr.io/wallet-service:latest
# ... repeat for other services

# 4. Restart to apply migrations
az containerapp restart --name wallet-service --resource-group sorcha-prod-rg
az containerapp restart --name tenant-service --resource-group sorcha-prod-rg
```

---

## What Gets Created?

### Databases

1. **Azure Database for PostgreSQL Flexible Server** (~$12/month)
   - `sorcha_wallet` database
   - `sorcha_tenant` database
   - Auto-backup (7 days)
   - SSL/TLS required

2. **Azure Cosmos DB (MongoDB API)** (Serverless, ~$1-5/month)
   - `sorcha_system_register` database
   - `transactions` collection

### Container Apps

All services deployed to Azure Container Apps (Consumption plan):
- `wallet-service` - Auto-scaled (0-3 replicas)
- `tenant-service` - Auto-scaled (1-5 replicas)
- `blueprint-api` - Auto-scaled (0-3 replicas)
- `peer-service` - Always running (1-3 replicas)
- `api-gateway` - Always running (1-5 replicas)
- `register-service` - Auto-scaled (0-3 replicas)
- `validator-service` - Auto-scaled (0-3 replicas)

### Other Resources

- Azure Cache for Redis (Basic C0) - ~$16/month
- Azure Container Registry (Basic) - ~$5/month
- Log Analytics Workspace - Pay-as-you-go

**Total Cost:** ~$35-45/month

---

## Verify Deployment

### Check Migrations Applied

```bash
# Tenant Service (should show seed data created)
az containerapp logs show \
  --name tenant-service \
  --resource-group sorcha-prod-rg \
  --tail 100 | grep -i "migration\|initialization"
```

**Expected output:**
```
[INF] Applying 2 pending migration(s)
[INF] Database migrations applied successfully
[INF] Creating default organization: Sorcha Local
[INF] Creating default administrator: admin@sorcha.local
[INF] Database initialization completed successfully
```

### Check Wallet Service

```bash
az containerapp logs show \
  --name wallet-service \
  --resource-group sorcha-prod-rg \
  --tail 50 | grep -i "migration"
```

**Expected output:**
```
Applying Wallet Service database migrations...
Wallet Service database migrations applied successfully
```

### Get API Gateway URL

```bash
az containerapp show \
  --name api-gateway \
  --resource-group sorcha-prod-rg \
  --query properties.configuration.ingress.fqdn \
  --output tsv
```

Visit this URL to access the API!

---

## Login Credentials

### Default Admin User

**⚠️ CHANGE THESE IMMEDIATELY IN PRODUCTION!**

- **Email:** `admin@sorcha.local`
- **Password:** `Dev_Pass_2025!`

### Service Principal Credentials

**Found in Tenant Service logs:**

```bash
az containerapp logs show \
  --name tenant-service \
  --resource-group sorcha-prod-rg \
  --tail 200 | grep -A 5 "Service Principal Created"
```

**Save these credentials!** They're only shown once.

---

## Common Issues

### Issue: "Database does not exist"

**Cause:** Infrastructure not deployed yet.

**Fix:**
```bash
# Re-deploy infrastructure
az deployment sub create \
  --location eastus \
  --template-file infra/main.bicep \
  --parameters resourceGroupName=sorcha-prod-rg ...
```

### Issue: "Migration pending changes warning"

**Cause:** New migration created locally but not in Docker image.

**Fix:**
```bash
# Rebuild and push image
docker-compose build wallet-service
docker tag sorcha/wallet-service:latest sorchacr.azurecr.io/wallet-service:latest
docker push sorchacr.azurecr.io/wallet-service:latest

# Restart
az containerapp restart --name wallet-service --resource-group sorcha-prod-rg
```

### Issue: Container Apps not starting

**Cause:** Waiting for database to be provisioned.

**Fix:**
```bash
# Check PostgreSQL status
az postgres flexible-server show \
  --name sorcha-postgres-prod \
  --resource-group sorcha-prod-rg \
  --query state

# Wait for "Ready" status, then restart apps
```

---

## Update Workflow

### Apply New Migration

```bash
# 1. Create migration locally
dotnet ef migrations add NewFeature --context WalletDbContext

# 2. Build and push
docker-compose build wallet-service
docker tag sorcha/wallet-service:latest sorchacr.azurecr.io/wallet-service:latest
docker push sorchacr.azurecr.io/wallet-service:latest

# 3. Restart (migration runs automatically)
az containerapp restart --name wallet-service --resource-group sorcha-prod-rg

# 4. Verify
az containerapp logs show --name wallet-service --resource-group sorcha-prod-rg --tail 50
```

**No manual SQL scripts needed!** EF Core applies migrations automatically.

---

## Cost Optimization

### Development/Staging

```bash
# Stop PostgreSQL when not in use
az postgres flexible-server stop \
  --name sorcha-postgres-prod \
  --resource-group sorcha-prod-rg

# Scale Container Apps to zero
az containerapp update \
  --name wallet-service \
  --resource-group sorcha-prod-rg \
  --min-replicas 0
```

**Savings:** ~50% reduction during off-hours

### Production

- Enable PostgreSQL reserved capacity (save 55%)
- Enable Cosmos DB reserved capacity (save 65%)
- Use Azure Hybrid Benefit if applicable

---

## Further Reading

- **Full Documentation:** [AZURE-DATABASE-INITIALIZATION.md](AZURE-DATABASE-INITIALIZATION.md)
- **Troubleshooting:** See full docs for detailed solutions
- **Security:** Key Vault integration, Managed Identity setup

---

## Summary

**Local Docker:**
```yaml
postgres:
  volumes:
    - ./docker/postgres-init.sql:/docker-entrypoint-initdb.d/01-init.sql
```

**Azure:**
```bicep
resource walletDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2023-12-01-preview' = {
  name: 'sorcha_wallet'
}
```

**Application Code:** Identical! Migrations run automatically in both environments.

🎉 **You're ready to deploy to Azure!**
