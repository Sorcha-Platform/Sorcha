# Administration

This guide covers day-to-day administration tasks: managing organizations and users, backup and restore procedures, and security hardening.

## Organization Management

### Create an Organization

```bash
TOKEN="<your-admin-jwt-token>"

curl -X POST http://localhost/tenant/api/organizations \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "name": "Acme Corporation",
    "subdomain": "acme",
    "contactEmail": "admin@acme.com"
  }'
```

### List Organizations

```bash
curl -X GET http://localhost/tenant/api/organizations \
  -H "Authorization: Bearer $TOKEN"
```

### Deactivate an Organization

```bash
curl -X DELETE http://localhost/tenant/api/organizations/<org-id> \
  -H "Authorization: Bearer $TOKEN"
```

Deactivation disables all users and participants in the organization. Data is retained but inaccessible.

## User Management

### Roles

| Role | Capabilities |
|------|-------------|
| `SystemAdmin` | Full platform access, all organizations, system configuration |
| `Administrator` | Organization management, user management within their org |
| `User` | Standard access, participate in workflows |

### Create a User

```bash
curl -X POST http://localhost/tenant/api/organizations/<org-id>/users \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "email": "user@example.com",
    "displayName": "Jane Smith",
    "password": "SecurePassword123!",
    "roles": ["User"]
  }'
```

### List Users in an Organization

```bash
curl -X GET "http://localhost/tenant/api/organizations/<org-id>/users" \
  -H "Authorization: Bearer $TOKEN"
```

### Deactivate a User

```bash
curl -X DELETE "http://localhost/tenant/api/organizations/<org-id>/users/<user-id>" \
  -H "Authorization: Bearer $TOKEN"
```

### Reset a User's Password

Users can change their own password via the API. Administrators can force a password reset by deactivating and recreating the user account.

## API Documentation Portal

The OpenAPI documentation is available at `/openapi` via the API Gateway.

### Access Control

By default, OpenAPI documentation requires authentication. Control this with:

```bash
# In .env
OPENAPI_REQUIRE_AUTH=true   # Require JWT (production)
OPENAPI_REQUIRE_AUTH=false  # Open access (development)
```

### Accessing the Portal

- **Open access:** Navigate to `http://localhost/openapi`
- **Authenticated:** Include a Bearer token in the request, or log in via the Sorcha UI and navigate to the API docs section

The documentation uses Scalar UI (not Swagger) and covers all service endpoints.

## Backup Procedures

### PostgreSQL Backup

PostgreSQL stores Wallet and Tenant data. Back up regularly.

**Full database dump:**

```bash
# Backup all databases
docker exec sorcha-postgres pg_dumpall -U sorcha > backup_postgres_$(date +%Y%m%d).sql

# Backup specific database (Tenant)
docker exec sorcha-postgres pg_dump -U sorcha sorcha_tenant > backup_tenant_$(date +%Y%m%d).sql

# Backup specific database (Wallet)
docker exec sorcha-postgres pg_dump -U sorcha sorcha_wallet > backup_wallet_$(date +%Y%m%d).sql
```

**Compressed backup:**

```bash
docker exec sorcha-postgres pg_dump -U sorcha -Fc sorcha_tenant > backup_tenant_$(date +%Y%m%d).dump
```

### MongoDB Backup

MongoDB stores Register and Blueprint data.

**Full dump:**

```bash
docker exec sorcha-mongodb mongodump \
  -u sorcha -p <password> \
  --authenticationDatabase admin \
  --out /backup/$(date +%Y%m%d)

# Copy backup from container
docker cp sorcha-mongodb:/backup ./mongodb-backup
```

**Specific database:**

```bash
docker exec sorcha-mongodb mongodump \
  -u sorcha -p <password> \
  --authenticationDatabase admin \
  --db sorcha_register_registry \
  --out /backup/registry
```

### Redis Backup

Redis data is primarily cache and can be regenerated, but backing up is still recommended:

```bash
# Trigger a background save
docker exec sorcha-redis redis-cli BGSAVE

# Copy the dump file
docker cp sorcha-redis:/data/dump.rdb ./redis-backup/dump_$(date +%Y%m%d).rdb
```

### Wallet Encryption Keys

The wallet encryption keys volume is critical. Without these keys, encrypted wallet data cannot be decrypted.

```bash
# Backup encryption keys volume
docker run --rm \
  -v sorcha_wallet-encryption-keys:/source:ro \
  -v $(pwd)/backup:/backup \
  alpine tar czf /backup/wallet-keys_$(date +%Y%m%d).tar.gz -C /source .
```

Store encryption key backups in a separate, secure location (e.g., encrypted external drive, secure vault).

### DataProtection Keys

ASP.NET Data Protection keys are used for cookie encryption and anti-forgery tokens:

```bash
docker run --rm \
  -v sorcha_dataprotection-keys:/source:ro \
  -v $(pwd)/backup:/backup \
  alpine tar czf /backup/dataprotection-keys_$(date +%Y%m%d).tar.gz -C /source .
```

### Automated Backup Script

Example cron job for daily backups:

```bash
#!/bin/bash
# /opt/sorcha/backup.sh
BACKUP_DIR="/opt/sorcha/backups/$(date +%Y%m%d)"
mkdir -p "$BACKUP_DIR"

# PostgreSQL
docker exec sorcha-postgres pg_dumpall -U sorcha > "$BACKUP_DIR/postgres.sql"

# MongoDB
docker exec sorcha-mongodb mongodump -u sorcha -p <password> \
  --authenticationDatabase admin --out /tmp/mongodump
docker cp sorcha-mongodb:/tmp/mongodump "$BACKUP_DIR/mongodb"

# Redis
docker exec sorcha-redis redis-cli BGSAVE
sleep 2
docker cp sorcha-redis:/data/dump.rdb "$BACKUP_DIR/redis.rdb"

# Encryption keys
docker run --rm -v sorcha_wallet-encryption-keys:/src:ro \
  -v "$BACKUP_DIR":/dst alpine cp -r /src /dst/wallet-keys

# Retention: keep 30 days
find /opt/sorcha/backups -maxdepth 1 -type d -mtime +30 -exec rm -rf {} \;
```

Add to crontab:
```bash
0 2 * * * /opt/sorcha/backup.sh >> /var/log/sorcha-backup.log 2>&1
```

## Restore Procedures

### PostgreSQL Restore

```bash
# Stop services that use PostgreSQL
docker-compose stop wallet-service tenant-service

# Restore from SQL dump
docker exec -i sorcha-postgres psql -U sorcha < backup_postgres_20260101.sql

# Or from compressed dump
docker exec -i sorcha-postgres pg_restore -U sorcha -d sorcha_tenant backup_tenant_20260101.dump

# Restart services
docker-compose start wallet-service tenant-service
```

### MongoDB Restore

```bash
# Stop services that use MongoDB
docker-compose stop blueprint-service register-service validator-service peer-service

# Copy backup into container
docker cp ./mongodb-backup sorcha-mongodb:/restore

# Restore
docker exec sorcha-mongodb mongorestore \
  -u sorcha -p <password> \
  --authenticationDatabase admin \
  /restore/20260101

# Restart services
docker-compose start blueprint-service register-service validator-service peer-service
```

### Redis Restore

```bash
# Stop Redis
docker-compose stop redis

# Copy dump file into volume
docker run --rm \
  -v sorcha_redis-data:/data \
  -v $(pwd)/redis-backup:/backup \
  alpine cp /backup/dump_20260101.rdb /data/dump.rdb

# Start Redis
docker-compose start redis
```

### Wallet Encryption Keys Restore

```bash
# Stop Wallet Service
docker-compose stop wallet-service

# Restore keys
docker run --rm \
  -v sorcha_wallet-encryption-keys:/dest \
  -v $(pwd)/backup:/backup \
  alpine sh -c "tar xzf /backup/wallet-keys_20260101.tar.gz -C /dest && chown -R 1654:1654 /dest"

# Start Wallet Service
docker-compose start wallet-service
```

## Security Hardening Checklist

### Immediate (Before First Use)

- [ ] Change the default admin password (`admin@sorcha.local` / `Dev_Pass_2025!`)
- [ ] Generate a unique `JWT_SIGNING_KEY` (see [Installation](installation-first-run.md#generate-a-jwt-signing-key))
- [ ] Change default database passwords (`POSTGRES_PASSWORD`, `MONGO_INITDB_ROOT_PASSWORD`)
- [ ] Set `OPENAPI_REQUIRE_AUTH=true`
- [ ] Set `ASPNETCORE_ENVIRONMENT=Production` (disables detailed error pages)

### Network Security

- [ ] Place the API Gateway behind a TLS-terminating reverse proxy or load balancer
- [ ] Block direct access to database ports (5432, 27017, 16379) from external networks
- [ ] Block direct access to the Aspire Dashboard (18888) from external networks
- [ ] Configure firewall rules to allow only necessary ports (see [Prerequisites](prerequisites-sizing.md#firewall-rules))
- [ ] Use Docker network isolation -- databases should not be accessible from the host in production

### Authentication & Authorization

- [ ] Rotate `JWT_SIGNING_KEY` periodically (quarterly recommended)
- [ ] Rotate service-to-service secrets (`ServiceAuth__ClientSecret`)
- [ ] Use strong passwords (minimum 12 characters, mixed case, numbers, symbols)
- [ ] Review and remove unused user accounts regularly
- [ ] Consider integrating with an external identity provider (OIDC/SAML) for SSO

### Data Protection

- [ ] Enable encrypted backups and store off-site
- [ ] Back up wallet encryption keys separately and securely
- [ ] Enable MongoDB authentication (already configured by default in Docker)
- [ ] Enable Redis authentication (`REDIS_PASSWORD`) in production
- [ ] Review Docker volume permissions

### Monitoring

- [ ] Set up health check alerts (see [Monitoring](monitoring-observability.md#alerting-recommendations))
- [ ] Enable audit logging for administrative actions
- [ ] Monitor for unusual patterns (failed auth attempts, bulk data access)
- [ ] Restrict Aspire Dashboard access to administrators only

### Container Security

- [ ] Keep Docker and base images updated
- [ ] Scan container images for vulnerabilities
- [ ] Run containers as non-root (Sorcha images already run as UID 1654)
- [ ] Use read-only file systems where possible
- [ ] Set resource limits (CPU, memory) per container
