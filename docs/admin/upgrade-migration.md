# Upgrade & Migration

Procedures for upgrading Sorcha to a new version, applying database migrations, and rolling back if needed.

## Version Upgrade Process

### Pre-Upgrade Checklist

- [ ] Read the release notes / changelog for breaking changes
- [ ] Back up all databases (see [Administration -- Backup Procedures](administration.md#backup-procedures))
- [ ] Back up wallet encryption keys
- [ ] Note the current running version: `docker-compose ps`
- [ ] Plan a maintenance window (upgrades may cause brief downtime)
- [ ] Test the upgrade in a staging environment first

### Standard Upgrade (Docker Compose)

#### Step 1: Pull Latest Code

```bash
cd /path/to/sorcha
git fetch origin
git checkout <release-tag>   # e.g., git checkout v1.0.1
```

#### Step 2: Back Up Databases

```bash
# PostgreSQL
docker exec sorcha-postgres pg_dumpall -U sorcha > backup_pre_upgrade_$(date +%Y%m%d).sql

# MongoDB
docker exec sorcha-mongodb mongodump \
  -u sorcha -p <password> \
  --authenticationDatabase admin \
  --out /backup/pre_upgrade_$(date +%Y%m%d)
docker cp sorcha-mongodb:/backup ./mongodb-backup-pre-upgrade
```

#### Step 3: Rebuild Images

```bash
# Rebuild all service images (--no-cache ensures fresh build)
docker-compose build --no-cache
```

#### Step 4: Apply Database Migrations

If the release includes Entity Framework Core migrations:

```bash
# Option A: Migrations run automatically on service startup (default behavior)
# Just proceed to Step 5.

# Option B: Run migrations manually before starting services
# Requires .NET 10 SDK on the host
dotnet ef database update \
  --project src/Services/Sorcha.Tenant.Service \
  --connection "Host=localhost;Port=5432;Database=sorcha_tenant;Username=sorcha;Password=<password>"

dotnet ef database update \
  --project src/Services/Sorcha.Wallet.Service \
  --connection "Host=localhost;Port=5432;Database=sorcha_wallet;Username=sorcha;Password=<password>"
```

#### Step 5: Start Updated Services

```bash
docker-compose up -d
```

Services start in dependency order. Monitor startup:

```bash
docker-compose logs -f
```

#### Step 6: Verify Health

```bash
# Check all containers are healthy
docker-compose ps

# Test health endpoints
curl http://localhost/health
curl http://localhost/blueprint/health
curl http://localhost/tenant/health
curl http://localhost/register/health
curl http://localhost/wallet/health
curl http://localhost/validator/health
curl http://localhost/peer/health
```

#### Step 7: Smoke Test

- Log in with admin credentials
- Verify existing data is accessible (organizations, blueprints, registers)
- Test a basic workflow end-to-end

## Database Migrations

### Entity Framework Core (PostgreSQL)

Sorcha uses EF Core for PostgreSQL databases (Tenant and Wallet services). Migrations are code-first and typically applied automatically on service startup.

**Check pending migrations:**

```bash
dotnet ef migrations list \
  --project src/Services/Sorcha.Tenant.Service

dotnet ef migrations list \
  --project src/Services/Sorcha.Wallet.Service
```

**Apply migrations manually:**

```bash
dotnet ef database update \
  --project src/Services/Sorcha.Tenant.Service \
  --connection "Host=localhost;Port=5432;Database=sorcha_tenant;Username=sorcha;Password=<password>"
```

**Generate a migration script (for review before applying):**

```bash
dotnet ef migrations script \
  --project src/Services/Sorcha.Tenant.Service \
  --idempotent \
  --output migration_tenant.sql
```

### MongoDB Schema Changes

MongoDB is schemaless, so there are no formal migrations. However, Sorcha may include data transformation scripts for major version upgrades:

- **Index changes:** The Register Service creates indexes on startup when `RegisterStorage__MongoDB__CreateIndexesOnStartup=true`.
- **Collection restructuring:** Applied via migration scripts in the `scripts/migrations/` directory (if applicable).
- **Document format changes:** Handled by the application layer with backward-compatible serialization.

Check for new indexes after upgrade:

```bash
docker exec sorcha-mongodb mongosh -u sorcha -p <password> --eval "
  use sorcha_register_registry;
  db.getCollectionNames().forEach(c => {
    print('--- ' + c + ' ---');
    printjson(db[c].getIndexes());
  });
"
```

## Rollback Procedures

### Quick Rollback (Revert to Previous Image)

If the upgrade fails and you need to roll back quickly:

```bash
# Stop services
docker-compose down

# Check out the previous version
git checkout <previous-tag>

# Rebuild with previous code
docker-compose build --no-cache

# Start services
docker-compose up -d
```

### Rollback with Database Restore

If database migrations were applied and need to be reversed:

```bash
# Stop all services
docker-compose down

# Restore PostgreSQL from pre-upgrade backup
docker-compose up -d postgres
sleep 10  # Wait for PostgreSQL to be ready
docker exec -i sorcha-postgres psql -U sorcha < backup_pre_upgrade_20260101.sql

# Restore MongoDB from pre-upgrade backup
docker-compose up -d mongodb
sleep 10  # Wait for MongoDB to be ready
docker cp ./mongodb-backup-pre-upgrade/pre_upgrade_20260101 sorcha-mongodb:/restore
docker exec sorcha-mongodb mongorestore \
  -u sorcha -p <password> \
  --authenticationDatabase admin \
  --drop \
  /restore/pre_upgrade_20260101

# Check out previous version and rebuild
git checkout <previous-tag>
docker-compose build --no-cache
docker-compose up -d
```

### EF Core Migration Rollback

To revert a specific EF Core migration:

```bash
# Revert to a specific migration
dotnet ef database update <PreviousMigrationName> \
  --project src/Services/Sorcha.Tenant.Service \
  --connection "Host=localhost;Port=5432;Database=sorcha_tenant;Username=sorcha;Password=<password>"
```

## Infrastructure Upgrades

### Docker Engine

```bash
# Check current version
docker version

# Update Docker Desktop (Windows/macOS): Download from docker.com
# Update Docker Engine (Linux):
sudo apt-get update && sudo apt-get install docker-ce docker-ce-cli containerd.io
```

After updating Docker, restart all containers:
```bash
docker-compose down
docker-compose up -d
```

### PostgreSQL Version Upgrade

Major PostgreSQL upgrades (e.g., 16 to 17) require a dump/restore:

```bash
# 1. Dump all data
docker exec sorcha-postgres pg_dumpall -U sorcha > postgres_full_dump.sql

# 2. Stop and remove old container + volume
docker-compose down
docker volume rm sorcha_postgres-data

# 3. Update image tag in docker-compose.yml (e.g., postgres:18-alpine)

# 4. Start new PostgreSQL
docker-compose up -d postgres
sleep 10

# 5. Restore data
docker exec -i sorcha-postgres psql -U sorcha < postgres_full_dump.sql

# 6. Start remaining services
docker-compose up -d
```

### MongoDB Version Upgrade

MongoDB supports in-place upgrades for sequential major versions (e.g., 7 to 8):

```bash
# 1. Back up data
docker exec sorcha-mongodb mongodump -u sorcha -p <password> --authenticationDatabase admin --out /backup

# 2. Update image tag in docker-compose.yml

# 3. Restart MongoDB
docker-compose up -d --force-recreate mongodb
```

For skipping major versions, dump and restore is required (similar to PostgreSQL process).

### Redis Version Upgrade

Redis upgrades are typically seamless:

```bash
# Update image tag in docker-compose.yml (e.g., redis:9-alpine)
docker-compose up -d --force-recreate redis
```

Redis persists data via RDB snapshots in the `redis-data` volume, which is compatible across minor and major versions.

## Breaking Changes Template

When reviewing release notes, watch for these categories:

| Category | Impact | Example |
|----------|--------|---------|
| **Environment variables renamed** | Update `.env` file | `JWT_KEY` renamed to `JWT_SIGNING_KEY` |
| **Database schema changes** | Run migrations before starting | New column in `UserIdentities` table |
| **API endpoint changes** | Update client integrations | `/api/v1/blueprints` changed to `/api/v2/blueprints` |
| **Configuration format changes** | Update `docker-compose.yml` | New required environment variable |
| **Removed features** | Review dependent workflows | Deprecated API removed |
| **Docker volume changes** | May require manual volume migration | New volume added for encryption keys |

## Upgrade Best Practices

1. **Always back up before upgrading.** Database backups are fast and prevent data loss.
2. **Read the changelog.** Pay attention to breaking changes and required manual steps.
3. **Test in staging first.** Deploy the new version to a staging environment before production.
4. **Use tagged releases.** Never run `latest` in production -- pin to specific version tags.
5. **Monitor after upgrade.** Watch health checks, error rates, and performance for 24 hours after upgrading.
6. **Keep rollback backups for 7 days.** Do not delete pre-upgrade backups until the new version is confirmed stable.
