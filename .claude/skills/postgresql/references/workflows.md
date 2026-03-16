# PostgreSQL Workflows Reference

## Contents
- Database Migrations
- Connection Configuration
- Health Checks
- Docker Initialization
- Troubleshooting

---

## Database Migrations

### Creating a Migration

```bash
# From solution root
cd src/Core/Sorcha.Wallet.Core
dotnet ef migrations add MyMigrationName --startup-project ../../Services/Sorcha.Wallet.Service
```

### Migration with Schema Creation

```csharp
// 20251207234439_InitialWalletSchema.cs
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.EnsureSchema(name: "wallet");
    
    migrationBuilder.CreateTable(
        name: "wallets",
        schema: "wallet",
        columns: table => new
        {
            Address = table.Column<string>(type: "text", nullable: false),
            Balance = table.Column<decimal>(type: "numeric(28,18)", nullable: false),
            Metadata = table.Column<Dictionary<string, string>>(type: "jsonb", nullable: true),
            CreatedAt = table.Column<DateTime>(
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP")
        },
        constraints: table => table.PrimaryKey("PK_wallets", x => x.Address));
}
```

### Applying Migrations

```bash
# Apply pending migrations
dotnet ef database update --startup-project ../../Services/Sorcha.Wallet.Service

# Apply specific migration
dotnet ef database update MyMigrationName --startup-project ../../Services/Sorcha.Wallet.Service

# Generate SQL script (for production)
dotnet ef migrations script --idempotent -o migrate.sql
```

### Migration Workflow Checklist

Copy this checklist and track progress:
- [ ] Create migration: `dotnet ef migrations add <Name>`
- [ ] Review generated migration code
- [ ] Test migration on local database
- [ ] Generate idempotent SQL script for review
- [ ] Apply to staging environment
- [ ] Verify application functionality
- [ ] Apply to production

---

## Connection Configuration

### Docker Compose

```yaml
# docker-compose.yml:42-63
postgres:
  image: postgres:17-alpine
  container_name: sorcha-postgres
  environment:
    POSTGRES_USER: sorcha
    POSTGRES_PASSWORD: sorcha_dev_password
    POSTGRES_DB: sorcha
  ports:
    - "5432:5432"
  volumes:
    - ./docker/postgres-init.sql:/docker-entrypoint-initdb.d/01-init.sql:ro
    - postgres_data:/var/lib/postgresql/data
  healthcheck:
    test: ["CMD-SHELL", "pg_isready -U sorcha"]
    interval: 10s
    timeout: 5s
    retries: 5
```

### Aspire Configuration

```csharp
// AppHost.cs:16-17
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin();  // pgAdmin at http://localhost:5050

var tenantDb = postgres.AddDatabase("tenant-db", "sorcha_tenant");
var walletDb = postgres.AddDatabase("wallet-db", "sorcha_wallet");
```

### Service Registration with Retry

```csharp
// WalletServiceExtensions.cs:98-109
services.AddDbContext<WalletDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 10,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorCodesToAdd: null);
        npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "wallet");
    });
    
    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
        options.EnableDetailedErrors();
    }
});
```

---

## Health Checks

### Adding PostgreSQL Health Check

```csharp
// WalletServiceExtensions.cs:140
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "wallet-postgresql", tags: ["ready"]);
```

### Health Check Endpoints

```csharp
// Program.cs
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false  // Liveness = always healthy if responding
});
```

### Validation Loop

1. Start the service
2. Check health: `curl http://localhost:7001/health/ready`
3. If unhealthy, check PostgreSQL connectivity
4. Verify connection string in configuration
5. Repeat health check until passing

---

## Docker Initialization

### Database Creation Script

```sql
-- docker/postgres-init.sql
SELECT 'CREATE DATABASE sorcha_wallet'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'sorcha_wallet')\gexec

SELECT 'CREATE DATABASE sorcha_tenant'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'sorcha_tenant')\gexec

GRANT ALL PRIVILEGES ON DATABASE sorcha_wallet TO sorcha;
GRANT ALL PRIVILEGES ON DATABASE sorcha_tenant TO sorcha;
```

### First-Time Setup

Copy this checklist and track progress:
- [ ] Start PostgreSQL: `docker-compose up -d postgres`
- [ ] Wait for health check: `docker-compose ps` (should show "healthy")
- [ ] Run migrations for Tenant: `dotnet ef database update --project src/Services/Sorcha.Tenant.Service`
- [ ] Run migrations for Wallet: `dotnet ef database update --project src/Services/Sorcha.Wallet.Service`
- [ ] Verify tables: `docker exec -it sorcha-postgres psql -U sorcha -d sorcha_wallet -c '\dt wallet.*'`

---

## Troubleshooting

### Connection Refused

**Symptoms:** `Npgsql.NpgsqlException: Failed to connect`

**Checks:**
1. Is PostgreSQL running? `docker-compose ps postgres`
2. Is port exposed? `docker-compose port postgres 5432`
3. Is firewall blocking? Test: `telnet localhost 5432`

### Migration Conflicts

**Symptoms:** `The migration has already been applied`

**Fix:**
```bash
# Remove last migration (if not applied to production)
dotnet ef migrations remove

# Or mark as applied without running
dotnet ef database update --no-build
```

### JSONB Serialization Empty

**Symptoms:** `Dictionary<string, string>` saves as `{}`

**Cause:** Missing `EnableDynamicJson()` in Npgsql 8.0+

**Fix:**
```csharp
services.AddNpgsqlDataSource(connectionString, dataSourceBuilder =>
{
    dataSourceBuilder.EnableDynamicJson();
});
```

### Slow Queries

**Diagnostic:**
```sql
-- Find slow queries
SELECT query, calls, mean_exec_time, total_exec_time
FROM pg_stat_statements
ORDER BY mean_exec_time DESC
LIMIT 10;
```

**Common Fixes:**
1. Add missing index on WHERE clause columns
2. Use `AsNoTracking()` for read-only queries
3. Replace N+1 with `Include()` eager loading

See the **entity-framework** skill for query optimization techniques.

---

## Connection String Format

```
Host=localhost;Port=5432;Database=sorcha_wallet;Username=sorcha;Password=your_password;
```

**Additional options:**
- `Pooling=true;MinPoolSize=5;MaxPoolSize=100` - Connection pooling
- `CommandTimeout=30` - Query timeout in seconds
- `SSL Mode=Require` - Force SSL (production)
- `Trust Server Certificate=true` - Dev only, skip cert validation