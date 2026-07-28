# Docker Compose MongoDB Configuration

**Date:** 2025-01-11
**Status:** ✅ Complete - MongoDB Enabled by Default

---

## Summary

The Register Service is now configured to use MongoDB by default when running via `docker-compose up`. This provides production-ready persistence for registers, transactions, and dockets.

---

## What Was Changed

### 1. Docker Compose Configuration (`docker-compose.yml`)

**MongoDB Environment Variables Added:**
```yaml
register-service:
  environment:
    # Configure MongoDB storage (production-ready persistence)
    RegisterStorage__Type: MongoDB
    RegisterStorage__MongoDB__ConnectionString: mongodb://sorcha:sorcha_dev_password@mongodb:27017
    RegisterStorage__MongoDB__DatabaseName: sorcha_register
    RegisterStorage__MongoDB__RegisterCollectionName: registers
    RegisterStorage__MongoDB__TransactionCollectionName: transactions
    RegisterStorage__MongoDB__DocketCollectionName: dockets
    RegisterStorage__MongoDB__CreateIndexesOnStartup: "true"
```

**Key Points:**
- ✅ `RegisterStorage__Type: MongoDB` - Enables MongoDB storage
- ✅ Connection string uses `mongodb://` hostname (Docker internal network)
- ✅ Database name: `sorcha_register`
- ✅ Automatic index creation enabled
- ✅ Service depends on MongoDB with health check

### 2. Dockerfile Update (`src/Services/Sorcha.Register.Service/Dockerfile`)

**MongoDB Storage Project Added:**
```dockerfile
# Copy project files for restore
COPY ["src/Core/Sorcha.Register.Storage.MongoDB/Sorcha.Register.Storage.MongoDB.csproj", "src/Core/Sorcha.Register.Storage.MongoDB/"]
```

**Purpose:**
- Ensures MongoDB storage library is included in Docker image
- Allows the service to load MongoDB repository at runtime
- Dependencies restored during build

---

## Configuration Behavior

### Docker Compose (Production Mode)
When running `docker-compose up`:
- ✅ Uses MongoDB storage (persistent)
- ✅ Data survives container restarts
- ✅ Connects to `mongodb` service via Docker network
- ✅ Indexes created automatically on first startup

**Startup Log:**
```
✅ Register Service using MongoDB storage: mongodb://sorcha:***@mongodb:27017
Now listening on: http://[::]:8080
```

### Local Development (Development Mode)
When running `dotnet run` locally:
- ✅ Uses InMemory storage (default)
- ✅ No MongoDB required
- ✅ Fast startup for development
- ✅ Data resets on each restart

**Startup Log:**
```
✅ Register Service using InMemory storage (development mode)
Now listening on: http://localhost:5290
```

---

## Architecture

### Docker Networking

```
┌─────────────────────────────────────────┐
│         Docker Network                  │
│                                         │
│  ┌──────────────────┐                  │
│  │  register-service│                  │
│  │  Port: 5290:8080 │                  │
│  │                  │                  │
│  │  Env:            │                  │
│  │  RegisterStorage │                  │
│  │    Type=MongoDB  │                  │
│  └────────┬─────────┘                  │
│           │                             │
│           │ mongodb://mongodb:27017     │
│           ▼                             │
│  ┌──────────────────┐                  │
│  │    mongodb       │                  │
│  │    Port: 27017   │                  │
│  │                  │                  │
│  │  Database:       │                  │
│  │  sorcha_register │                  │
│  └──────────────────┘                  │
│                                         │
└─────────────────────────────────────────┘
```

**Connection Flow:**
1. Register Service starts
2. Reads `RegisterStorage__Type=MongoDB` from environment
3. Loads `MongoRegisterRepository` instead of `InMemoryRegisterRepository`
4. Connects to `mongodb://mongodb:27017` (Docker DNS)
5. Creates database `sorcha_register`
6. Creates indexes on startup
7. Service ready to accept requests

---

## Environment Variables Reference

### MongoDB Storage Configuration

| Variable | Value | Purpose |
|----------|-------|---------|
| `RegisterStorage__Type` | `MongoDB` | Enable MongoDB storage |
| `RegisterStorage__MongoDB__ConnectionString` | `mongodb://sorcha:sorcha_dev_password@mongodb:27017` | MongoDB connection |
| `RegisterStorage__MongoDB__DatabaseName` | `sorcha_register` | Database name |
| `RegisterStorage__MongoDB__RegisterCollectionName` | `registers` | Collection for registers |
| `RegisterStorage__MongoDB__TransactionCollectionName` | `transactions` | Collection for transactions |
| `RegisterStorage__MongoDB__DocketCollectionName` | `dockets` | Collection for dockets |
| `RegisterStorage__MongoDB__CreateIndexesOnStartup` | `"true"` | Auto-create indexes |

### Connection String Components

```
mongodb://username:password@hostname:port
          ├──────┘ ├──────┘ ├──────┘ └──┘
          │        │        │         └─── Port (27017)
          │        │        └───────────── Docker hostname
          │        └────────────────────── Password
          └─────────────────────────────── Username
```

**Docker Compose Values:**
- **Username:** `sorcha`
- **Password:** `sorcha_dev_password` (development only!)
- **Hostname:** `mongodb` (Docker service name)
- **Port:** `27017` (MongoDB default)

---

## Testing

### Quick Test Script

Run the provided test script:
```bash
pwsh walkthroughs/RegisterMongoDB/test-docker-compose.ps1
```

**What it tests:**
1. ✅ Docker is running
2. ✅ Register Service image builds
3. ✅ MongoDB container starts
4. ✅ Register Service container starts
5. ✅ MongoDB configuration detected in logs
6. ✅ API responds to health checks
7. ✅ Environment variables set correctly

### Manual Testing

**1. Start Services:**
```bash
docker-compose up -d mongodb register-service
```

**2. Check Logs:**
```bash
docker logs sorcha-register-service
```

**Expected Output:**
```
✅ Register Service using MongoDB storage: mongodb://sorcha:***@mongodb:27017
Creating MongoDB indexes for Register storage
Now listening on: http://[::]:8080
```

**3. Test API:**
```bash
curl http://localhost:5290/health
curl http://localhost:5290/api/registers
```

**4. Check MongoDB:**
```bash
# Connect to MongoDB
docker exec -it sorcha-mongodb mongosh sorcha_register --username sorcha --password sorcha_dev_password

# List collections
show collections

# Check indexes
db.registers.getIndexes()
db.transactions.getIndexes()
db.dockets.getIndexes()
```

**Expected Collections:**
- `registers`
- `transactions`
- `dockets`

**Expected Indexes:**
- Total: 14 indexes (3 on registers, 3 on transactions, 2 on dockets, plus default `_id` indexes)

---

## Production Deployment

### Security Considerations

⚠️ **IMPORTANT: Change default credentials for production!**

**Current (Development):**
```yaml
ConnectionString: mongodb://sorcha:sorcha_dev_password@mongodb:27017
```

**Production:**
```yaml
ConnectionString: mongodb://${MONGO_USER}:${MONGO_PASSWORD}@mongodb:27017
```

**Recommendations:**
1. Use secrets management (Docker Secrets, Azure Key Vault, AWS Secrets Manager)
2. Enable TLS/SSL for MongoDB connections
3. Use strong passwords (32+ characters)
4. Enable MongoDB authentication and authorization
5. Configure MongoDB replica sets for high availability
6. Set up MongoDB backup and restore procedures

### Environment-Specific Configuration

**Staging:**
```yaml
RegisterStorage__MongoDB__ConnectionString: ${STAGING_MONGO_CONNECTION}
RegisterStorage__MongoDB__DatabaseName: sorcha_register_staging
```

**Production:**
```yaml
RegisterStorage__MongoDB__ConnectionString: ${PROD_MONGO_CONNECTION}
RegisterStorage__MongoDB__DatabaseName: sorcha_register_prod
RegisterStorage__MongoDB__CreateIndexesOnStartup: "false"  # Pre-create indexes manually
```

---

## Switching Between Storage Modes

### Use MongoDB (Docker)
```bash
# Already configured by default
docker-compose up -d
```

### Use InMemory (Override)
```bash
# Override in docker-compose.override.yml
services:
  register-service:
    environment:
      RegisterStorage__Type: InMemory
```

### Use InMemory (Local Development)
```bash
# Just run locally without setting MongoDB type
dotnet run --project src/Services/Sorcha.Register.Service
```

---

## Troubleshooting

### Issue: "MongoDB connection failed"

**Symptoms:**
```
MongoDB.Driver.MongoConnectionException: Unable to connect to server
```

**Solutions:**
1. Check MongoDB is running: `docker ps | grep mongodb`
2. Check MongoDB logs: `docker logs sorcha-mongodb`
3. Verify network: `docker network inspect sorcha_sorcha-network`
4. Check connection string hostname is `mongodb` (not `localhost`)

### Issue: "Service starts with InMemory instead of MongoDB"

**Symptoms:**
```
✅ Register Service using InMemory storage (development mode)
```

**Solutions:**
1. Check environment variable is set: `docker exec sorcha-register-service printenv | grep RegisterStorage`
2. Verify docker-compose.yml has `RegisterStorage__Type: MongoDB`
3. Rebuild image: `docker-compose build register-service`
4. Restart service: `docker-compose restart register-service`

### Issue: "Indexes not created"

**Symptoms:**
```javascript
db.registers.getIndexes()  // Only shows _id index
```

**Solutions:**
1. Check `CreateIndexesOnStartup: "true"` is set
2. Check service logs for index creation messages
3. Manually create indexes (see MongoDB documentation)
4. Verify MongoDB user has index creation permissions

---

## Files Modified

### Configuration Files (2)
1. `docker-compose.yml` - Added MongoDB environment variables
2. `src/Services/Sorcha.Register.Service/Dockerfile` - Added MongoDB project reference

### Test Files (2)
3. `walkthroughs/RegisterMongoDB/test-docker-compose.ps1` - Docker Compose test script
4. `walkthroughs/RegisterMongoDB/DOCKER-COMPOSE-CONFIG.md` - This documentation

---

## Verification Checklist

- [x] MongoDB environment variables added to docker-compose.yml
- [x] RegisterStorage__Type set to "MongoDB"
- [x] Connection string uses Docker hostname "mongodb"
- [x] Database name configured (sorcha_register)
- [x] Collection names configured
- [x] CreateIndexesOnStartup enabled
- [x] Service depends on MongoDB with health check
- [x] Dockerfile includes MongoDB storage project
- [x] Test script created
- [x] Documentation complete

---

## Related Documentation

- [MongoDB Integration Guide](README.md)
- [Register Service Specification](../../.specify/specs/sorcha-register-service.md)
- [Docker Compose Reference](../../docker-compose.yml)

---

## Next Steps

1. **Test the Configuration:**
   ```bash
   pwsh walkthroughs/RegisterMongoDB/test-docker-compose.ps1
   ```

2. **Start All Services:**
   ```bash
   docker-compose up -d
   ```

3. **Monitor Logs:**
   ```bash
   docker logs -f sorcha-register-service
   ```

4. **Test API Operations:**
   - Create a register
   - Submit transactions
   - Query data
   - Verify persistence (restart container and check data)

5. **Update Documentation:**
   - Mark MongoDB integration complete in MASTER-TASKS.md
   - Update development-status.md
   - Update README.md

---

**Configuration Completed By:** Claude (AI Assistant)
**Status:** ✅ Ready for Testing
**Docker Compose:** ✅ MongoDB Enabled by Default
