# Sorcha Troubleshooting Guide

## Common Issues and Solutions

### 1. Port Already in Use Errors

**Symptoms:**
```
error: could not start the proxy for the service: listen tcp [::1]:8080: bind:
Only one usage of each socket address is normally permitted.
```

**Cause:**
- Previous dotnet processes didn't shut down cleanly
- Ports are still bound from previous run

**Solutions:**

#### Quick Fix - Use Cleanup Script (Easiest)
```powershell
# Windows PowerShell
.\scripts\cleanup-ports.ps1

# Or Git Bash/Unix/Mac
bash scripts/cleanup-ports.sh
```

#### Manual Fix - Kill Specific Process
```powershell
# Windows - Find process on port
netstat -ano | findstr ":8080"

# Kill the process (replace PID with actual number)
taskkill /F /PID 12345
```

```bash
# Unix/Mac - Find and kill process
lsof -ti:8080 | xargs kill -9
```

#### Nuclear Option - Kill All .NET Processes
```powershell
# Windows
taskkill /F /IM dotnet.exe

# Unix/Mac
killall -9 dotnet
```

### 2. Aspire DCP Timeout Errors

**Symptoms:**
```
fail: Aspire.Hosting.Dcp.dcpctrl.dcpctrl.ServiceReconciler[0]
      Service status update failed
      error": "Timeout: request did not complete within requested timeout"
```

**Causes:**
- Services taking longer than expected to start
- Docker Desktop resource constraints
- First-time container image downloads

**Solutions:**

#### A. Increase Docker Resources (Recommended)
1. Open Docker Desktop
2. Go to Settings → Resources
3. Increase:
   - **CPUs**: At least 4 cores
   - **Memory**: At least 8 GB
   - **Swap**: At least 2 GB
4. Click "Apply & Restart"

#### B. Wait Longer
- These are warnings, not errors
- Services often start successfully despite timeouts
- Wait 2-3 minutes and check the Aspire dashboard
- Dashboard URL is shown in console: `https://localhost:17256`

#### C. Start Services Individually
```bash
# Start just the backend services first
cd src/Apps/Orchestration/Sorcha.AppHost
dotnet run

# Wait for all services to show "Running" in dashboard
# Then access:
# - Gateway: Check dashboard for port
# - Blazor: Check dashboard for port
```

#### D. Clean Docker State
```bash
# Stop all containers
docker stop $(docker ps -aq)

# Remove Aspire containers
docker rm $(docker ps -aq -f name=redis)
docker rm $(docker ps -aq -f name=rediscommander)

# Restart AppHost
cd src/Apps/Orchestration/Sorcha.AppHost
dotnet run
```

### 2. Port Conflicts

**Symptoms:**
```
Error: Failed to bind to address https://localhost:7082
```

**Solutions:**

#### Check Ports in Use
```bash
# Windows
netstat -ano | findstr "8050 8060 8070 8080"

# Kill process (replace PID)
taskkill /F /PID <PID>
```

#### Use Different Ports
Edit `launchSettings.json` for each service or let Aspire assign dynamic ports.

### 3. Build Errors with File Locks

**Symptoms:**
```
error MSB3027: Could not copy "Sorcha.ServiceDefaults.dll"
The file is locked by: "Sorcha.Blueprint.Api (12345)"
```

**Solutions:**

#### Stop All Running Services
```bash
# Windows - Stop all dotnet processes
taskkill /F /IM dotnet.exe

# Or use Task Manager to end dotnet.exe processes
```

#### Clean and Rebuild
```bash
dotnet clean
dotnet build
```

### 4. Redis Connection Errors

**Symptoms:**
```
StackExchange.Redis.RedisConnectionException: No connection is available
```

**Solutions:**

#### Verify Docker is Running
```bash
docker ps | grep redis
```

#### Restart Redis via AppHost
The AppHost will automatically start Redis. Just restart the AppHost.

#### Manual Redis Start (if needed)
```bash
docker run -d -p 6379:6379 redis:8.2
```

### 5. Blazor Client Not Connecting to Gateway

**Symptoms:**
- Blazor app loads but API calls fail
- CORS errors in browser console
- 404 errors when calling `/api/*` endpoints

**Solutions:**

#### Check ApiConfiguration
File: `src/Apps/UI/Sorcha.Blueprint.Designer.Client/Services/ApiConfiguration.cs`

```csharp
public static string GatewayBaseUrl { get; set; } = "https://localhost:8061";
```

Update the port to match what Aspire assigned to the gateway.

#### Check Aspire Dashboard
1. Open dashboard: `https://localhost:17256`
2. Find `api-gateway` service
3. Note the HTTPS endpoint (e.g., `https://localhost:8061`)
4. Update `GatewayBaseUrl` to match

### 6. Integration Tests Failing

**Symptoms:**
```
Test run failed: DistributedApplication could not start
```

**Solutions:**

#### Stop Running Services First
Integration tests start their own services:
```bash
# Stop AppHost if running
# Then run tests:
dotnet test tests/Sorcha.Gateway.Integration.Tests
```

#### Check Docker Resources
Tests need Docker for Redis:
- Ensure Docker Desktop is running
- Ensure at least 4GB RAM allocated

#### Increase Test Timeout
Edit test if needed:
```csharp
[Fact(Timeout = 300000)] // 5 minutes
public async Task MyTest() { ... }
```

### 7. OpenAPI/Swagger Not Loading

**Symptoms:**
- `/scalar/v1` returns 404
- `/openapi/v1.json` returns 404

**Solutions:**

#### Check Environment
OpenAPI is only enabled in Development:
```bash
# Set environment variable
$env:ASPNETCORE_ENVIRONMENT="Development"

# Or in launchSettings.json:
"environmentVariables": {
  "ASPNETCORE_ENVIRONMENT": "Development"
}
```

#### Verify Gateway is Running
```bash
curl https://localhost:8061/api/health
```

### 8. Peer Service Not Discovering Peers

**Symptoms:**
- `/api/peer/peers` returns empty list
- Peer dashboard shows no connections

**Solutions:**

This is expected in development - peer discovery requires:
1. Multiple peer service instances running
2. Network configuration for peer-to-peer communication
3. Production deployment for actual peer network

For development, the service will run but show no peers.

## Diagnostic Commands

### Check All Services Status
```bash
# Via Aspire Dashboard
# Open: https://localhost:17256

# Via curl
curl http://localhost:8061/api/health
curl http://localhost:8061/api/stats
```

### Check Logs
```bash
# Logs are in Aspire dashboard
# Or check individual service logs in bin/Debug/net10.0/
```

### Verify Docker
```bash
docker ps
docker stats
```

### Verify .NET
```bash
dotnet --info
dotnet --list-sdks
```

## Performance Tuning

### Reduce Startup Time

1. **Disable Unused Services**
   Comment out services in AppHost.cs if not needed

2. **Use Release Build**
   ```bash
   dotnet run -c Release --project src/Apps/Orchestration/Sorcha.AppHost
   ```

3. **Increase Docker Resources**
   More RAM/CPU = faster startup

### Reduce Memory Usage

1. **Stop Services When Not Needed**
   Don't leave AppHost running when not developing

2. **Use Minimal Configuration**
   Only run services you're actively working on

## Getting Help

If issues persist:

1. **Check Logs**: Look in Aspire dashboard for detailed error messages
2. **GitHub Issues**: https://github.com/yourusername/sorcha/issues
3. **Aspire Docs**: https://learn.microsoft.com/dotnet/aspire/
4. **Clean Slate**: Delete `bin/`, `obj/`, restart Docker, rebuild

## Known Limitations

- **First Run Slow**: Initial Docker image download takes time
- **Windows Defender**: May slow file access - add exclusions for project folder
- **VPN Issues**: Some VPNs interfere with Docker networking
- **WSL2 Performance**: File I/O in WSL2 can be slower than native Windows
