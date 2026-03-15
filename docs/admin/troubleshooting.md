# Troubleshooting

Common issues, diagnostic procedures, and fixes for Sorcha deployments.

## Common Issues

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| 401 Unauthorized on all requests | JWT signing key mismatch between services | Verify `JWT_SIGNING_KEY` is identical across all services in `.env` |
| 401 after restart | Token expired (8-hour lifetime) | Request a new token via `/tenant/api/auth/login` |
| Service unavailable (502/503) | Container not started or unhealthy | `docker-compose ps` to check status, then check logs |
| Database connection refused | PostgreSQL/MongoDB not ready or wrong credentials | Check health: `docker-compose ps postgres`, verify credentials in `.env` |
| Wallet creation fails with permission error | Encryption key volume has wrong ownership | Run `docker run --rm -v sorcha_wallet-encryption-keys:/data alpine chown -R 1654:1654 /data` |
| SignalR connections drop | Redis not available for backplane | Check Redis health: `docker exec sorcha-redis redis-cli ping` |
| OpenAPI page returns 401 | `OPENAPI_REQUIRE_AUTH=true` with no token | Set `OPENAPI_REQUIRE_AUTH=false` for development, or provide a Bearer token |
| Container restart loop | Dependency not healthy, config error | Check logs: `docker-compose logs <service>` |
| MongoDB auth failure | Wrong credentials in connection string | Verify `MONGO_INITDB_ROOT_USERNAME`/`PASSWORD` matches connection strings |
| Slow startup (> 2 min) | Health check retries exhausting | Normal for first build; subsequent starts are faster |
| `ERR_CONNECTION_REFUSED` in browser | API Gateway not running or wrong port | Verify gateway is healthy: `docker-compose ps api-gateway` |
| CORS errors in browser | API Gateway misconfigured | Check `ASPNETCORE_ENVIRONMENT` -- `Development` enables permissive CORS |
| Register data missing | System register not bootstrapped | System register bootstraps automatically on startup. Check register-service logs for bootstrap errors. |

## Diagnostic Commands

### Container Status

```bash
# Overview of all containers
docker-compose ps

# Detailed status with health
docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"

# Show resource usage
docker stats --no-stream
```

### Viewing Logs

```bash
# All services, follow mode
docker-compose logs -f

# Specific service, last 200 lines
docker-compose logs --tail=200 blueprint-service

# Filter for errors
docker-compose logs tenant-service 2>&1 | grep -i "error\|exception\|fail"

# Logs since a specific time
docker-compose logs --since="2026-03-01T10:00:00" wallet-service
```

### Network Debugging

```bash
# Test DNS resolution between containers
docker exec sorcha-blueprint-service nslookup wallet-service

# Test connectivity from one service to another
docker exec sorcha-blueprint-service wget -qO- http://wallet-service:8080/health

# List all containers on the Sorcha network
docker network inspect sorcha_sorcha-network | jq '.[].Containers'

# Check published ports
docker port sorcha-api-gateway
```

### Database Diagnostics

**PostgreSQL:**

```bash
# Connect to PostgreSQL
docker exec -it sorcha-postgres psql -U sorcha

# Check active connections
docker exec sorcha-postgres psql -U sorcha -c "SELECT count(*) FROM pg_stat_activity;"

# Check database sizes
docker exec sorcha-postgres psql -U sorcha -c "SELECT pg_database.datname, pg_size_pretty(pg_database_size(pg_database.datname)) FROM pg_database;"

# List tables in tenant database
docker exec sorcha-postgres psql -U sorcha -d sorcha_tenant -c "\dt public.*"
```

**MongoDB:**

```bash
# Connect to MongoDB
docker exec -it sorcha-mongodb mongosh -u sorcha -p <password>

# List databases
docker exec sorcha-mongodb mongosh -u sorcha -p <password> --eval "db.adminCommand('listDatabases')"

# Check database stats
docker exec sorcha-mongodb mongosh -u sorcha -p <password> --eval "use sorcha_register_registry; db.stats()"

# Check collection counts
docker exec sorcha-mongodb mongosh -u sorcha -p <password> --eval "use sorcha_register_registry; db.getCollectionNames().forEach(c => print(c + ': ' + db[c].countDocuments()))"
```

**Redis:**

```bash
# Ping Redis
docker exec sorcha-redis redis-cli ping

# Check memory usage
docker exec sorcha-redis redis-cli info memory

# List all keys (development only -- do not use in production with large datasets)
docker exec sorcha-redis redis-cli keys "*"

# Check connected clients
docker exec sorcha-redis redis-cli info clients
```

## Service-Specific Diagnostics

### API Gateway

**Issue: Routes not forwarding**

```bash
# Check if upstream services are reachable from the gateway
docker exec sorcha-api-gateway wget -qO- http://blueprint-service:8080/health
docker exec sorcha-api-gateway wget -qO- http://tenant-service:8080/health
```

The API Gateway uses YARP for reverse proxying. Route configuration is loaded from the service's `appsettings.json`. Verify the `Services__*__Url` environment variables match the actual container names.

### Tenant Service

**Issue: Bootstrap data missing**

```bash
# Check if organizations exist
docker exec sorcha-postgres psql -U sorcha -d sorcha_tenant \
  -c "SELECT \"Id\", \"Name\", \"Status\" FROM public.\"Organizations\";"

# Check if admin user exists
docker exec sorcha-postgres psql -U sorcha -d sorcha_tenant \
  -c "SELECT \"Id\", \"Email\", \"Status\" FROM public.\"UserIdentities\";"
```

If empty, the bootstrap seeder may have failed. Check Tenant Service logs:
```bash
docker-compose logs tenant-service | grep -i "seed\|bootstrap"
```

### Wallet Service

**Issue: Encryption errors**

```bash
# Check if key volume is accessible
docker exec sorcha-wallet-service ls -la /var/lib/sorcha/wallet-keys/

# Check volume permissions
docker run --rm -v sorcha_wallet-encryption-keys:/data alpine ls -la /data/
```

The wallet service runs as UID 1654. The key directory must be owned by this user:
```bash
docker run --rm -v sorcha_wallet-encryption-keys:/data alpine chown -R 1654:1654 /data
```

### Register Service

**Issue: MongoDB connection errors**

```bash
# Verify MongoDB is accepting connections
docker exec sorcha-mongodb mongosh -u sorcha -p <password> --eval "db.runCommand({ping: 1})"

# Check if register databases exist
docker exec sorcha-mongodb mongosh -u sorcha -p <password> --eval "db.adminCommand('listDatabases').databases.filter(d => d.name.startsWith('sorcha_register'))"
```

## Debug Mode

Enable detailed error responses and verbose logging:

```bash
# In .env or docker-compose override
ASPNETCORE_ENVIRONMENT=Development
```

In Development mode:
- Detailed exception pages are returned in API responses
- SQL queries are logged (PostgreSQL/EF Core)
- CORS is permissive
- OpenAPI documentation is accessible without auth (unless overridden)

**Warning:** Never use `Development` environment in production. It exposes internal details in error responses.

## Resetting the Environment

### Full Reset (All Data)

```bash
# Stop all containers and remove volumes
docker-compose down -v

# Rebuild and start fresh
docker-compose up -d --build
```

This destroys all data including databases, encryption keys, and cache. The Tenant Service will re-run bootstrap seeding on startup.

### Partial Reset (Keep Infrastructure)

```bash
# Stop application services only
docker-compose stop blueprint-service wallet-service register-service tenant-service validator-service peer-service api-gateway

# Remove application containers
docker-compose rm -f blueprint-service wallet-service register-service tenant-service validator-service peer-service api-gateway

# Rebuild and restart
docker-compose up -d --build
```

### Reset a Single Service

```bash
# Rebuild and restart one service
docker-compose build <service-name> --no-cache
docker-compose up -d --force-recreate <service-name>
```

## Getting Help

1. Check the [Aspire Dashboard](http://localhost:18888) for traces and structured logs
2. Review the [Configuration Reference](configuration-reference.md) for environment variable details
3. Check the [Port Configuration](../getting-started/PORT-CONFIGURATION.md) for correct port assignments
4. Review the [Bootstrap Credentials](../getting-started/BOOTSTRAP-CREDENTIALS.md) for default accounts
