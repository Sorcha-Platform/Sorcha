# Bootstrap Credentials - Development Environment

**Generated:** 2025-12-13 19:59:48 UTC
**Status:** ✅ Active
**Environment:** Development Only

---

## ⚠️ Security Warning

**These are DEVELOPMENT credentials only!**
- ❌ NEVER use these credentials in production
- ❌ NEVER commit credentials to source control
- ✅ Credentials are stored in `.env.local` (gitignored)
- ✅ Secrets are SHA256-hashed in database

---

## Default Organization & Admin User

The Tenant Service bootstrap process creates a default organization and administrator user for local development.

### Organization

| Property | Value |
|----------|-------|
| **ID** | `00000000-0000-0000-0000-000000000001` |
| **Name** | Sorcha Local |
| **Subdomain** | sorcha-local |
| **Status** | Active |

### Admin User

| Property | Value |
|----------|-------|
| **ID** | `00000000-0000-0000-0001-000000000001` |
| **Email** | admin@sorcha.local |
| **Password** | Dev_Pass_2025! |
| **Display Name** | System Administrator |
| **Role** | Administrator |
| **Status** | Active |

**Login:**
```bash
curl -X POST https://localhost:7080/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "admin@sorcha.local",
    "password": "Dev_Pass_2025!"
  }'
```

---

## Service Principal Credentials

Service principals enable service-to-service authentication using OAuth2 client credentials flow. Each service has a unique client ID and client secret.

### Blueprint Service

| Property | Value |
|----------|-------|
| **ID** | `00000000-0000-0000-0002-000000000001` |
| **Service Name** | Blueprint Service |
| **Client ID** | `service-blueprint` |
| **Client Secret** | `s5CeyuJs9tRtBnPIElPesrRsBhqvyYRtaxmAineg01w` |
| **Scopes** | blueprints:read, blueprints:write, wallets:sign, register:write |
| **Status** | Active |

**Get Token:**
```bash
curl -X POST https://localhost:7080/api/service-auth/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials" \
  -d "client_id=service-blueprint" \
  -d "client_secret=s5CeyuJs9tRtBnPIElPesrRsBhqvyYRtaxmAineg01w"
```

### Wallet Service

| Property | Value |
|----------|-------|
| **ID** | `00000000-0000-0000-0002-000000000002` |
| **Service Name** | Wallet Service |
| **Client ID** | `service-wallet` |
| **Client Secret** | `tSa2Ve2O5SgxchxqihK8Z6HcXaTE8EWDcVvXwH6QKfE` |
| **Scopes** | wallets:read, wallets:write, wallets:sign, wallets:encrypt, wallets:decrypt |
| **Status** | Active |

**Get Token:**
```bash
curl -X POST https://localhost:7080/api/service-auth/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials" \
  -d "client_id=service-wallet" \
  -d "client_secret=tSa2Ve2O5SgxchxqihK8Z6HcXaTE8EWDcVvXwH6QKfE"
```

### Register Service

| Property | Value |
|----------|-------|
| **ID** | `00000000-0000-0000-0002-000000000003` |
| **Service Name** | Register Service |
| **Client ID** | `service-register` |
| **Client Secret** | `8C6_gYRSk5WMvOARyK4L1csueinifQ9o3UbupEqYvCo` |
| **Scopes** | registers:read, registers:write, registers:query |
| **Status** | Active |

**Get Token:**
```bash
curl -X POST https://localhost:7080/api/service-auth/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials" \
  -d "client_id=service-register" \
  -d "client_secret=8C6_gYRSk5WMvOARyK4L1csueinifQ9o3UbupEqYvCo"
```

### Peer Service

| Property | Value |
|----------|-------|
| **ID** | `00000000-0000-0000-0002-000000000004` |
| **Service Name** | Peer Service |
| **Client ID** | `service-peer` |
| **Client Secret** | `nK0EcSnd6sDMKfDPLL3K8aUrEUfjuBPvw2Sk55wH2zI` |
| **Scopes** | peers:read, peers:write, registers:read |
| **Status** | Active |

**Get Token:**
```bash
curl -X POST https://localhost:7080/api/service-auth/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials" \
  -d "client_id=service-peer" \
  -d "client_secret=nK0EcSnd6sDMKfDPLL3K8aUrEUfjuBPvw2Sk55wH2zI"
```

---

## OAuth2 Token Response

All service token requests return the following JSON response:

```json
{
  "access_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "token_type": "Bearer",
  "expires_in": 28800,
  "scope": "blueprints:read blueprints:write wallets:sign register:write"
}
```

**Token Properties:**
- **Lifetime:** 8 hours (28,800 seconds)
- **Type:** Bearer token (use in Authorization header)
- **Algorithm:** HS256 (HMAC-SHA256)
- **Issuer:** https://localhost:7080
- **Audience:** Service-specific

**Using the Token:**
```bash
curl -X GET https://localhost:7081/api/blueprints \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
```

---

## Resetting Bootstrap Data

If you need to regenerate credentials or reset the bootstrap data:

### Method 1: Reset Database (Complete Wipe)

```bash
# Stop all services
docker-compose -f docker-compose.infrastructure.yml down -v

# Start infrastructure (PostgreSQL will be empty)
docker-compose -f docker-compose.infrastructure.yml up -d

# Wait 10 seconds for containers to be healthy
Start-Sleep -Seconds 10

# Start Tenant Service (will run bootstrap seeding)
dotnet run --project src/Services/Sorcha.Tenant.Service

# NEW credentials will be shown in logs - save them to .env.local
```

⚠️ **Warning:** This deletes ALL data including custom users and organizations!

### Method 2: Manual Deletion (Selective)

```bash
# Connect to PostgreSQL
docker exec -it sorcha-postgres psql -U sorcha -d sorcha_tenant

# Delete service principals only (keeps org and users)
DELETE FROM public."ServicePrincipals";

# Delete admin user only
DELETE FROM public."UserIdentities" WHERE "Id" = '00000000-0000-0000-0001-000000000001';

# Delete default org (cascades to all users)
DELETE FROM public."Organizations" WHERE "Id" = '00000000-0000-0000-0000-000000000001';

# Exit psql
\q

# Restart Tenant Service to re-run seeding
dotnet run --project src/Services/Sorcha.Tenant.Service
```

---

## Verification

Verify bootstrap data exists in PostgreSQL:

### Check Organization

```bash
docker exec sorcha-postgres psql -U sorcha -d sorcha_tenant \
  -c "SELECT \"Id\", \"Name\", \"Subdomain\", \"Status\" FROM public.\"Organizations\";"
```

**Expected Output:**
```
                  Id                  |     Name     |  Subdomain   | Status
--------------------------------------+--------------+--------------+--------
 00000000-0000-0000-0000-000000000001 | Sorcha Local | sorcha-local | Active
```

### Check Admin User

```bash
docker exec sorcha-postgres psql -U sorcha -d sorcha_tenant \
  -c "SELECT \"Id\", \"Email\", \"DisplayName\", \"Status\", \"Roles\" FROM public.\"UserIdentities\";"
```

**Expected Output:**
```
                  Id                  |       Email        |     DisplayName      | Status |     Roles
--------------------------------------+--------------------+----------------------+--------+---------------
 00000000-0000-0000-0001-000000000001 | admin@sorcha.local | System Administrator | Active | Administrator
```

### Check Service Principals

```bash
docker exec sorcha-postgres psql -U sorcha -d sorcha_tenant \
  -c "SELECT \"Id\", \"ServiceName\", \"ClientId\", \"Status\" FROM public.\"ServicePrincipals\";"
```

**Expected Output:**
```
                  Id                  |    ServiceName    |     ClientId      | Status
--------------------------------------+-------------------+-------------------+--------
 00000000-0000-0000-0002-000000000001 | Blueprint Service | service-blueprint | Active
 00000000-0000-0000-0002-000000000002 | Wallet Service    | service-wallet    | Active
 00000000-0000-0000-0002-000000000003 | Register Service  | service-register  | Active
 00000000-0000-0000-0002-000000000004 | Peer Service      | service-peer      | Active
```

---

## Troubleshooting

### "Token is invalid or has expired"

**Problem:** Service token expired (8 hour lifetime)

**Solution:** Request a new token using the client credentials:
```bash
curl -X POST https://localhost:7080/api/service-auth/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials" \
  -d "client_id=service-blueprint" \
  -d "client_secret=YOUR_SECRET"
```

### "Invalid client credentials"

**Problem:** Client secret is incorrect or service principal doesn't exist

**Solution:**
1. Check credentials in `.env.local`
2. Verify service principal exists in database
3. If needed, delete and re-run bootstrap seeding

### "Database initialization failed"

**Problem:** PostgreSQL connection timeout (Windows/Docker Desktop)

**Solution:** Update connection string to use `host.docker.internal`:
```
Host=host.docker.internal;Port=5432;Database=sorcha_tenant;...
```

---

## Security Best Practices

### Development

- ✅ Keep `.env.local` in `.gitignore`
- ✅ Use different secrets for each developer
- ✅ Rotate secrets monthly
- ✅ Never hardcode credentials in code
- ✅ Use environment variables or configuration files

### Production

- ✅ **NEVER** use development credentials
- ✅ Use Azure Key Vault or AWS Secrets Manager
- ✅ Rotate secrets automatically (90 days)
- ✅ Use separate service accounts per environment
- ✅ Enable audit logging for all credential access
- ✅ Use managed identities when possible

---

## Related Documentation

- [Infrastructure Setup Guide](INFRASTRUCTURE-SETUP.md) - PostgreSQL, Redis, MongoDB deployment
- [Authentication Setup Guide](../guides/AUTHENTICATION-SETUP.md) - JWT configuration and service integration
- [Tenant Service Specification](.specify/specs/sorcha-tenant-service.md) - Complete API documentation

---

**Document Version:** 1.0
**Last Updated:** 2025-12-13
**Owner:** Sorcha Contributors
**Status:** ✅ Bootstrap seeding complete and verified
