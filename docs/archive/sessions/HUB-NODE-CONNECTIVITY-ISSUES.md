# Hub Node Connectivity Issues - Analysis Report

**Date**: 2024-12-24
**Scope**: Local Docker Desktop and Azure deployment
**Status**: Issues Identified

---

## Summary

The Sorcha peer network has connectivity issues in both local Docker and potentially Azure deployments. The Docker peer service is running in "isolated mode" because it cannot reach configured hub nodes.

---

## Issue 1: Local Docker - No Local Hub Node

### Current State

**Docker Container Status:**
- ✅ `sorcha-peer-service` - Running (port 5002 HTTP, 5003 gRPC)
- ✅ Health endpoint responding: `http://localhost:5002/health`
- ⚠️  Operating in "isolated mode" - no hub connections

**Configuration:**
```json
{
  "PeerService": {
    "CentralNode": {
      "IsCentralNode": false,
      "CentralNodes": [
        { "NodeId": "n0.sorcha.dev", "Hostname": "n0.sorcha.dev", "Port": 5000, "Priority": 0 },
        { "NodeId": "n1.sorcha.dev", "Hostname": "n1.sorcha.dev", "Port": 5000, "Priority": 1 },
        { "NodeId": "n2.sorcha.dev", "Hostname": "n2.sorcha.dev", "Port": 5000, "Priority": 2 }
      ]
    }
  }
}
```

**Log Evidence:**
```
warn: Cannot perform sync - no active hub node connection
warn: All hub nodes unreachable - operating in isolated mode
info: Isolated mode active - peer will continue serving cached blueprints
```

### Root Cause

The peer service is configured as a **regular peer node** (not a hub) and expects to connect to external hub nodes at:
- `n0.sorcha.dev:5000`
- `n1.sorcha.dev:5000`
- `n2.sorcha.dev:5000`

These DNS names are not accessible from the local Docker environment:
1. **DNS Resolution**: Names don't resolve to local containers
2. **No Local Hub**: Docker Compose doesn't include a local hub node container
3. **Firewall/Network**: External Azure hub nodes may not be accessible yet

### Impact

- ⚠️  Peer service works but in degraded "isolated mode"
- ❌ Cannot sync system register from hub
- ❌ Cannot test peer-to-hub connectivity locally
- ❌ Cannot test incremental sync features
- ❌ Cannot test heartbeat monitoring
- ❌ Cannot test push notifications
- ✅ Can still serve cached blueprints (if any exist)
- ✅ Local REST API endpoints work

### Solution Options

#### Option A: Add Local Hub Node to Docker Compose (Recommended for Development)

Add a second peer service container configured as a hub node:

```yaml
services:
  peer-hub-local:
    build:
      context: .
      dockerfile: src/Services/Sorcha.Peer.Service/Dockerfile
    container_name: sorcha-peer-hub-local
    ports:
      - "5004:8080"  # HTTP
      - "5005:5000"  # gRPC
    environment:
      PeerService__NodeId: "hub-local.sorcha.dev"
      PeerService__CentralNode__IsCentralNode: "true"
      PeerService__CentralNode__ValidateHostname: "false"
      MongoDB__ConnectionString: "mongodb://sorcha:sorcha_dev_password@mongodb:27017"
    depends_on:
      - mongodb
```

Then update `peer-service` to connect to local hub:

```yaml
  peer-service:
    environment:
      # Add local hub node
      PeerService__CentralNode__CentralNodes__0__NodeId: "hub-local.sorcha.dev"
      PeerService__CentralNode__CentralNodes__0__Hostname: "peer-hub-local"
      PeerService__CentralNode__CentralNodes__0__Port: "5000"
      PeerService__CentralNode__CentralNodes__0__Priority: "0"
```

#### Option B: Configure Existing Peer as Hub Node

Convert the existing peer service to a hub node:

```yaml
  peer-service:
    environment:
      PeerService__CentralNode__IsCentralNode: "true"
      # Remove or comment out CentralNodes array
```

**Trade-off**: This gives you a hub, but you can't test peer-to-hub connectivity locally.

#### Option C: Use External Azure Hub (Requires Azure Deployment)

Keep configuration as-is and ensure Azure n0.sorcha.dev is accessible:
1. Verify Azure deployment is running
2. Configure firewall rules to allow traffic
3. Update local `/etc/hosts` or DNS if needed

---

## Issue 2: CLI Profile Configuration Mismatch

### Current State

The CLI has profiles configured, but some don't match actual Docker ports:

**"docker" Profile** (CLI Configuration):
```json
{
  "peerServiceUrl": "http://localhost:8080/peer",
  "tenantServiceUrl": "http://localhost:8080/tenant"
}
```

**Actual Docker Ports**:
- API Gateway: `http://localhost:8080` ✅
- Peer Service Direct REST: `http://localhost:5002` ⚠️
- Peer Service Direct gRPC: `http://localhost:5003` ⚠️

### Impact

- ✅ CLI can access peer service through API gateway at `/peer` route
- ⚠️  CLI cannot access peer service directly on port 5002/5003
- ⚠️  Unclear if API gateway routes `/peer` to peer service correctly

### Solution

1. **Test existing "docker" profile** - it may work through gateway
2. **Add "docker-direct" profile** for direct peer service access:

```json
{
  "docker-direct": {
    "peerServiceUrl": "http://localhost:5002",
    "walletServiceUrl": "http://localhost:5001",
    "tenantServiceUrl": "http://localhost:5110",
    "registerServiceUrl": "http://localhost:5290"
  }
}
```

---

## Issue 3: Azure Hub Node Status Unknown

### Current State

- Infrastructure deployed: `n0.sorcha.dev` configured in Bicep templates
- DNS status: Unknown (may not be configured yet)
- Container App status: Unknown
- Connectivity: Unknown

### Investigation Needed

1. Check if Azure Container Apps are running:
   ```bash
   az containerapp list --resource-group sorcha --output table
   ```

2. Check peer service logs:
   ```bash
   az containerapp logs show --name peer-service --resource-group sorcha --tail 100
   ```

3. Test DNS resolution:
   ```bash
   nslookup n0.sorcha.dev
   ```

4. Test connectivity:
   ```bash
   curl https://n0.sorcha.dev/health
   grpcurl n0.sorcha.dev:443 list
   ```

### Expected Configuration (Azure)

```json
{
  "PeerService": {
    "NodeId": "n0.sorcha.dev",
    "PublicAddress": "<azure-container-app-fqdn>",
    "CentralNode": {
      "IsCentralNode": true,
      "ValidateHostname": false,
      "Priority": 0
    }
  }
}
```

---

## Testing Checklist

### Local Docker Testing

- [ ] Add local hub node to `docker-compose.yml`
- [ ] Update peer service to connect to local hub
- [ ] Rebuild and restart containers: `docker-compose up -d --build`
- [ ] Verify hub node logs show "Central node initialized"
- [ ] Verify peer node logs show "Connected to hub node"
- [ ] Test CLI `sorcha peer list --profile docker`
- [ ] Test CLI `sorcha peer stats --profile docker`
- [ ] Test CLI `sorcha peer health --profile docker`
- [ ] Verify system register sync working

### Azure Hub Node Testing

- [ ] Check Azure Container Apps status
- [ ] Verify DNS for n0.sorcha.dev
- [ ] Test health endpoint: `https://n0.sorcha.dev/health`
- [ ] Check Azure logs for connection attempts
- [ ] Test CLI `sorcha peer list --profile staging`
- [ ] Test CLI against Azure API gateway
- [ ] Verify firewall rules allow gRPC traffic (port 443)

---

## Terminology Update Required

All references to "hub node" should be updated to "hub node":

**Files Affected**: 97 files including:
- `src/Services/Sorcha.Peer.Service/**/*.cs`
- `src/Services/Sorcha.Peer.Service/Protos/*.proto`
- `docker-compose.yml`
- `infra/*.bicep`
- `docs/*.md`

**Recommended Approach**:
1. Fix connectivity issues first
2. Verify peer network working
3. Create comprehensive terminology update PR
4. Use find/replace with careful review

---

## Next Steps

1. **Immediate**:
   - Add local hub node to Docker Compose
   - Test CLI against local Docker setup
   - Check Azure hub node status

2. **Short Term**:
   - Fix any identified connectivity issues
   - Document working configurations
   - Update CLI profiles as needed

3. **Medium Term**:
   - Complete terminology update (central → hub)
   - Update all documentation
   - Add hub node monitoring to CLI

---

## Related Files

- **Docker**: `docker-compose.yml`
- **Peer Service**: `src/Services/Sorcha.Peer.Service/appsettings.json`
- **CLI**: `src/Apps/Sorcha.Cli/Services/ConfigurationService.cs`
- **Infrastructure**: `infra/resources.bicep`
- **Documentation**: `docs/AZURE-CUSTOM-DOMAIN-SETUP.md`
