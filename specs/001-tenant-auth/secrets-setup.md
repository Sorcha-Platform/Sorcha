# Secrets Management Setup Guide

**Feature**: 001-tenant-auth
**Service**: Sorcha.Tenant.Service
**Date**: 2025-11-22
**Purpose**: Developer-friendly guide for setting up local secrets and production key management

---

## Overview

The Tenant Service handles sensitive cryptographic material and credentials that must NEVER be committed to source control. This guide explains how to manage secrets securely in both local development and production environments.

### Security Principles

1. **Never commit secrets** to Git (enforced by .gitignore)
2. **Use User Secrets** for local development (stored outside project directory)
3. **Use Azure Key Vault** (or AWS Secrets Manager) for production
4. **Rotate keys regularly** (especially JWT signing keys)
5. **Use strong, randomly generated keys** (never use weak passwords)

---

## Local Development Setup (5 Minutes)

### Prerequisites

- .NET 10 SDK installed
- Docker Desktop (for PostgreSQL and Redis)
- PowerShell or Bash terminal

### Step 1: Initialize User Secrets (Already Done)

The project has already been initialized with User Secrets:

```bash
# Verify User Secrets ID
dotnet user-secrets list --project src/Services/Sorcha.Tenant.Service
```

**UserSecretsId**: `25c124db-c85b-45b3-b9c0-a32827d91c0e`

### Step 2: Generate JWT Signing Key

The service uses **RS256 (RSA-SHA256)** for JWT signing, which requires a private/public key pair. Generate a secure key:

#### Option A: Using OpenSSL (Recommended)

```bash
# Generate RSA 4096-bit private key
openssl genrsa -out jwt_private.pem 4096

# Extract public key
openssl rsa -in jwt_private.pem -pubout -out jwt_public.pem

# View private key (PEM format)
cat jwt_private.pem

# View public key
cat jwt_public.pem
```

#### Option B: Using PowerShell (.NET)

```powershell
# Generate RSA key pair
$rsa = [System.Security.Cryptography.RSA]::Create(4096)
$privateKey = [System.Convert]::ToBase64String($rsa.ExportRSAPrivateKey())
$publicKey = [System.Convert]::ToBase64String($rsa.ExportRSAPublicKey())

Write-Host "Private Key (Base64):"
Write-Host $privateKey
Write-Host ""
Write-Host "Public Key (Base64):"
Write-Host $publicKey
```

#### Option C: Development-Only Key (Quick Start)

For **local development only**, you can use a pre-generated development key:

```bash
# WARNING: DEVELOPMENT ONLY - DO NOT USE IN PRODUCTION
dotnet user-secrets set "JwtSettings:SigningKey" "DEV_KEY_MIIEowIBAAKCAQEA..." --project src/Services/Sorcha.Tenant.Service
```

**Note**: See [development-keys.md](development-keys.md) for a pre-generated development key (if available).

### Step 3: Set Database Password

```bash
# Set PostgreSQL password for local development
dotnet user-secrets set "ConnectionStrings:Password" "YourSecurePassword123!" --project src/Services/Sorcha.Tenant.Service
```

**Important**: This password will be injected into the connection string at runtime.

### Step 4: Set Redis Password (Optional)

For local Redis without authentication (default Docker setup), this is not needed. For production-like Redis:

```bash
dotnet user-secrets set "Redis:Password" "YourRedisPassword" --project src/Services/Sorcha.Tenant.Service
```

### Step 5: Set External IDP Client Secrets (When Needed)

When configuring external identity providers (Azure Entra, AWS Cognito), store client secrets:

```bash
# Example: Azure Entra client secret
dotnet user-secrets set "ExternalIdp:AzureEntra:ClientSecret" "your-azure-client-secret" --project src/Services/Sorcha.Tenant.Service

# Example: AWS Cognito client secret
dotnet user-secrets set "ExternalIdp:AwsCognito:ClientSecret" "your-cognito-secret" --project src/Services/Sorcha.Tenant.Service
```

### Step 6: Verify Secrets Configuration

```bash
# List all configured secrets (values are hidden)
dotnet user-secrets list --project src/Services/Sorcha.Tenant.Service

# Expected output:
# JwtSettings:SigningKey = [Hidden]
# ConnectionStrings:Password = [Hidden]
# Redis:Password = [Hidden]
```

### Step 7: Update Connection String in Code

The `ConnectionStrings:TenantDatabase` in `appsettings.Development.json` intentionally omits the password. The service will inject the password from User Secrets at runtime.

**Configuration merge** (automatic):
```json
// appsettings.Development.json
{
  "ConnectionStrings": {
    "TenantDatabase": "Host=localhost;Port=5432;Database=sorcha_tenant_dev;Username=sorcha_user;Include Error Detail=true"
  }
}

// User Secrets (secrets.json)
{
  "ConnectionStrings": {
    "Password": "YourSecurePassword123!"
  }
}

// Merged at runtime:
// Host=localhost;Port=5432;Database=sorcha_tenant_dev;Username=sorcha_user;Password=YourSecurePassword123!;Include Error Detail=true
```

---

## How Secrets Are Loaded (Technical Details)

### Configuration Priority (Highest to Lowest)

1. **Command-line arguments** (`dotnet run --ConnectionStrings:Password="..."`)
2. **Environment variables** (`export ConnectionStrings__Password="..."`)
3. **User Secrets** (development only, stored in `%APPDATA%\Microsoft\UserSecrets\25c124db-c85b-45b3-b9c0-a32827d91c0e\secrets.json`)
4. **appsettings.{Environment}.json** (`appsettings.Development.json`)
5. **appsettings.json** (base configuration)

### User Secrets Storage Location

**Windows**:
`%APPDATA%\Microsoft\UserSecrets\25c124db-c85b-45b3-b9c0-a32827d91c0e\secrets.json`

**macOS/Linux**:
`~/.microsoft/usersecrets/25c124db-c85b-45b3-b9c0-a32827d91c0e/secrets.json`

This file is **outside the project directory** and will **never be committed** to Git.

### Accessing Secrets in Code

```csharp
// Program.cs or Startup.cs
var jwtSigningKey = builder.Configuration["JwtSettings:SigningKey"];
var dbPassword = builder.Configuration["ConnectionStrings:Password"];

// Using IOptions<T> pattern (recommended)
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
```

---

## Production Setup (Azure Key Vault)

### Why Azure Key Vault?

- **Centralized secret management** across multiple services
- **Automatic secret rotation** (e.g., rotate JWT keys every 90 days)
- **Audit logging** (track all secret access)
- **Managed identities** (no need to store vault credentials)
- **High availability** and disaster recovery

### Prerequisites

1. Azure subscription
2. Azure CLI installed (`az login`)
3. Permissions to create Key Vault resources

### Step 1: Create Azure Key Vault

```bash
# Login to Azure
az login

# Create resource group (if not exists)
az group create --name sorcha-production-rg --location eastus

# Create Key Vault
az keyvault create \
  --name sorcha-tenant-kv \
  --resource-group sorcha-production-rg \
  --location eastus \
  --enable-rbac-authorization false \
  --enabled-for-deployment true
```

### Step 2: Store Secrets in Key Vault

```bash
# Store JWT signing key
az keyvault secret set \
  --vault-name sorcha-tenant-kv \
  --name JwtSettings--SigningKey \
  --value "your-production-jwt-private-key-pem"

# Store database password
az keyvault secret set \
  --vault-name sorcha-tenant-kv \
  --name ConnectionStrings--Password \
  --value "your-production-db-password"

# Store Redis password
az keyvault secret set \
  --vault-name sorcha-tenant-kv \
  --name Redis--Password \
  --value "your-redis-password"
```

**Note**: Use `--` (double dash) instead of `:` for hierarchical keys in Azure Key Vault.

### Step 3: Grant Access to Managed Identity

```bash
# Create managed identity for App Service
az identity create \
  --name sorcha-tenant-service-identity \
  --resource-group sorcha-production-rg

# Get the principal ID
PRINCIPAL_ID=$(az identity show --name sorcha-tenant-service-identity --resource-group sorcha-production-rg --query principalId -o tsv)

# Grant Key Vault access
az keyvault set-policy \
  --name sorcha-tenant-kv \
  --object-id $PRINCIPAL_ID \
  --secret-permissions get list
```

### Step 4: Configure appsettings.Production.json

```json
{
  "AzureKeyVault": {
    "Enabled": true,
    "VaultUri": "https://sorcha-tenant-kv.vault.azure.net/",
    "UseDefaultCredential": true
  }
}
```

### Step 5: Update Program.cs to Load Key Vault

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add Azure Key Vault configuration
if (builder.Configuration.GetValue<bool>("AzureKeyVault:Enabled"))
{
    var vaultUri = builder.Configuration["AzureKeyVault:VaultUri"];
    var credential = new DefaultAzureCredential();

    builder.Configuration.AddAzureKeyVault(
        new Uri(vaultUri),
        credential);
}
```

### Step 6: Deploy to Azure App Service with Managed Identity

```bash
# Create App Service
az webapp create \
  --name sorcha-tenant-service \
  --resource-group sorcha-production-rg \
  --plan sorcha-app-plan \
  --runtime "DOTNETCORE:10.0"

# Assign managed identity
az webapp identity assign \
  --name sorcha-tenant-service \
  --resource-group sorcha-production-rg

# Grant Key Vault access to App Service
APP_PRINCIPAL_ID=$(az webapp identity show --name sorcha-tenant-service --resource-group sorcha-production-rg --query principalId -o tsv)

az keyvault set-policy \
  --name sorcha-tenant-kv \
  --object-id $APP_PRINCIPAL_ID \
  --secret-permissions get list
```

---

## AWS Secrets Manager (Alternative to Azure)

### Step 1: Create Secrets in AWS

```bash
# Store JWT signing key
aws secretsmanager create-secret \
  --name /sorcha/tenant/jwt-signing-key \
  --secret-string "your-production-jwt-private-key-pem"

# Store database password
aws secretsmanager create-secret \
  --name /sorcha/tenant/db-password \
  --secret-string "your-production-db-password"
```

### Step 2: Configure appsettings.Production.json

```json
{
  "AWS": {
    "Region": "us-east-1",
    "SecretName": "/sorcha/tenant"
  }
}
```

### Step 3: Update Program.cs

```csharp
// Add AWS Secrets Manager NuGet package:
// dotnet add package AWSSDK.SecretsManager

// Program.cs
var builder = WebApplication.CreateBuilder(args);

if (builder.Configuration.GetValue<bool>("AWS:Enabled"))
{
    var region = builder.Configuration["AWS:Region"];
    var secretName = builder.Configuration["AWS:SecretName"];

    var client = new AmazonSecretsManagerClient(RegionEndpoint.GetBySystemName(region));
    var request = new GetSecretValueRequest { SecretId = secretName };
    var response = await client.GetSecretValueAsync(request);

    var secrets = JsonSerializer.Deserialize<Dictionary<string, string>>(response.SecretString);
    builder.Configuration.AddInMemoryCollection(secrets);
}
```

---

## Secret Rotation Strategy

### JWT Signing Key Rotation

**Recommended**: Rotate JWT signing keys every **90 days**.

1. Generate new RSA key pair
2. Store new key in Key Vault (or User Secrets for dev)
3. Update `appsettings.json` to support **multiple signing keys** (old + new)
4. Wait for all old tokens to expire (1 hour + grace period)
5. Remove old key from configuration

**Multi-key configuration** (supports rotation):

```json
{
  "JwtSettings": {
    "SigningKeys": [
      {
        "KeyId": "key-2025-11",
        "PrivateKey": "...",
        "ValidFrom": "2025-11-01T00:00:00Z",
        "ValidUntil": "2026-02-01T00:00:00Z"
      },
      {
        "KeyId": "key-2026-02",
        "PrivateKey": "...",
        "ValidFrom": "2026-01-15T00:00:00Z",
        "ValidUntil": null
      }
    ]
  }
}
```

### Database Password Rotation

1. Create new database user with temporary password
2. Grant same permissions as existing user
3. Update Key Vault with new password
4. Restart service (picks up new password)
5. Verify service connectivity
6. Drop old database user

### IDP Client Secret Rotation

1. Generate new client secret in Azure Entra / AWS Cognito
2. Store new secret in Key Vault
3. Update organization configuration via Admin API
4. Wait 24 hours (grace period)
5. Delete old client secret from IDP

---

## Troubleshooting

### Error: "Unable to load User Secrets"

**Cause**: User Secrets not initialized or wrong project path.

**Solution**:
```bash
dotnet user-secrets init --project src/Services/Sorcha.Tenant.Service
```

### Error: "JwtSettings:SigningKey not found"

**Cause**: Signing key not set in User Secrets.

**Solution**:
```bash
dotnet user-secrets set "JwtSettings:SigningKey" "your-base64-encoded-key" --project src/Services/Sorcha.Tenant.Service
```

### Error: "Azure Key Vault access denied"

**Cause**: Managed Identity not granted Key Vault permissions.

**Solution**:
```bash
# Get App Service principal ID
az webapp identity show --name sorcha-tenant-service --resource-group sorcha-production-rg --query principalId

# Grant access
az keyvault set-policy \
  --name sorcha-tenant-kv \
  --object-id <principal-id> \
  --secret-permissions get list
```

### Error: "Database password authentication failed"

**Cause**: User Secrets password doesn't match PostgreSQL.

**Solution**:
1. Verify PostgreSQL password: `docker exec -it postgres psql -U sorcha_user`
2. Update User Secrets: `dotnet user-secrets set "ConnectionStrings:Password" "correct-password"`

---

## Security Best Practices

### DO

✅ **Use strong, randomly generated keys** (use `openssl rand -base64 32` or PowerShell `[System.Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))`)
✅ **Rotate secrets regularly** (JWT keys every 90 days, passwords every 180 days)
✅ **Use managed identities** in production (avoid storing vault credentials)
✅ **Enable audit logging** for Key Vault access
✅ **Use different secrets** for dev/staging/production environments
✅ **Store secrets in secure locations** (User Secrets for dev, Key Vault for prod)

### DON'T

❌ **Never commit secrets** to Git (even in private repos)
❌ **Never use weak passwords** like "password123" or "admin"
❌ **Never share production secrets** via email/Slack/Teams
❌ **Never reuse secrets** across environments
❌ **Never log secrets** (filter out sensitive values in Serilog)
❌ **Never hardcode secrets** in appsettings.json

---

## Quick Reference Commands

```bash
# List all user secrets
dotnet user-secrets list --project src/Services/Sorcha.Tenant.Service

# Set a secret
dotnet user-secrets set "Key:Name" "value" --project src/Services/Sorcha.Tenant.Service

# Remove a secret
dotnet user-secrets remove "Key:Name" --project src/Services/Sorcha.Tenant.Service

# Clear all secrets
dotnet user-secrets clear --project src/Services/Sorcha.Tenant.Service

# Generate random key (PowerShell)
[System.Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))

# Generate RSA key pair (OpenSSL)
openssl genrsa -out private.pem 4096
openssl rsa -in private.pem -pubout -out public.pem
```

---

## Next Steps

1. ✅ **Set up User Secrets** for local development (Steps 1-6 above)
2. ✅ **Start Docker dependencies**: `docker-compose up -d postgres redis`
3. ✅ **Run database migrations**: `dotnet ef database update --project src/Services/Sorcha.Tenant.Service`
4. ✅ **Run the service**: `dotnet run --project src/Services/Sorcha.Tenant.Service`
5. ✅ **Test endpoints**: Open https://localhost:7080/scalar

For full quickstart guide, see [quickstart.md](quickstart.md).

---

**Last Updated**: 2025-11-22
**Maintained By**: Sorcha Security Team
**Questions?**: See [TROUBLESHOOTING.md](../../docs/TROUBLESHOOTING.md)
