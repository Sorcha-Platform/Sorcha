# Quick Start Guide - Peer Service Integration Tests

Get up and running with the Peer Service integration tests in under 5 minutes.

## 🚀 Quick Start

### 1. Prerequisites Check

```bash
# Verify .NET 10 is installed
dotnet --version
# Should output: 10.0.x or later
```

### 2. Run All Tests

**Windows (PowerShell):**
```powershell
cd c:\projects\Sorcha\tests\Sorcha.Peer.Service.Integration.Tests
.\run-integration-tests.ps1
```

**Linux/macOS (Bash):**
```bash
cd /path/to/Sorcha/tests/Sorcha.Peer.Service.Integration.Tests
chmod +x run-integration-tests.sh
./run-integration-tests.sh
```

**Or use dotnet directly:**
```bash
dotnet test
```

✅ **Expected output:** All 36+ tests should pass in ~50 seconds

---

## 📋 Common Scenarios

### Run Specific Test Category

**Discovery tests only:**
```bash
dotnet test --filter "FullyQualifiedName~PeerDiscoveryTests"
```

**Communication tests only:**
```bash
dotnet test --filter "FullyQualifiedName~PeerCommunicationTests"
```

**Throughput/performance tests only:**
```bash
dotnet test --filter "FullyQualifiedName~PeerThroughputTests"
```

**Health check tests only:**
```bash
dotnet test --filter "FullyQualifiedName~PeerHealthTests"
```

### Run Single Test

```bash
dotnet test --filter "RegisterPeer_Via_REST_Should_Return_Success"
```

### Run with Verbose Output

**PowerShell:**
```powershell
.\run-integration-tests.ps1 -Verbose
```

**Bash:**
```bash
./run-integration-tests.sh --verbose
```

**Or:**
```bash
dotnet test --logger "console;verbosity=detailed"
```

### Generate Code Coverage

**PowerShell:**
```powershell
.\run-integration-tests.ps1 -Coverage
```

**Bash:**
```bash
./run-integration-tests.sh --coverage
```

This will:
1. Run all tests with coverage collection
2. Generate HTML coverage report
3. Automatically open the report in your browser

---

## 🧪 Test Categories Overview

### 1️⃣ Discovery Tests (12 tests)
Tests peer registration, lookup, and management.

**Key functionality:**
- Peer registration via REST and gRPC
- Auto-generated peer IDs
- Peer discovery and listing
- Peer metadata management

**Example test:**
```bash
dotnet test --filter "Multiple_Peers_Can_Discover_Each_Other"
```

### 2️⃣ Communication Tests (10 tests)
Tests bidirectional streaming between peers.

**Key functionality:**
- Single and multiple transaction processing
- Large payload handling (up to 1 MB)
- Concurrent streams
- Multi-peer messaging

**Example test:**
```bash
dotnet test --filter "Bidirectional_Stream_Should_Work_Correctly"
```

### 3️⃣ Throughput Tests (8 tests)
Performance and load testing.

**Key functionality:**
- High-volume processing (1000+ tx/sec)
- Sustained load testing
- Memory leak detection
- Parallel peer throughput

**Example test:**
```bash
dotnet test --filter "High_Volume_Transaction_Stream_Should_Maintain_Performance"
```

### 4️⃣ Health Tests (6 tests)
Service health and metrics validation.

**Key functionality:**
- Health endpoint checks
- Metrics accuracy
- REST/gRPC consistency
- Uptime tracking

**Example test:**
```bash
dotnet test --filter "Health_Endpoint_Should_Return_Healthy_Status"
```

---

## 🔍 Understanding Test Output

### Successful Run
```
✓ All tests passed! (Duration: 00:47)

Test Summary:
  Total: 36
  Passed: 36
  Failed: 0
  Skipped: 0
```

### Performance Metrics
Throughput tests output performance data:
```
Throughput: 234.56 transactions/second
Total time: 4.27 seconds
Average latency: 4.26 ms
Memory usage: 45.23 MB
```

### Failed Test
```
✗ Test Failed: RegisterPeer_Via_REST_Should_Return_Success
  Expected response.StatusCode to be Created, but found BadRequest

  Stack Trace:
    at PeerDiscoveryTests.cs:line 42
```

---

## 🛠️ Troubleshooting

### Problem: Port conflicts

**Symptom:** Tests fail with `AddressAlreadyInUseException`

**Solution:**
```bash
# Windows - Find processes using ports 5000-6000
netstat -ano | findstr "5"

# Linux/macOS
lsof -i :5000-6000

# Kill specific process (Windows)
taskkill /PID <process_id> /F

# Kill specific process (Linux/macOS)
kill -9 <process_id>
```

### Problem: Timeout errors

**Symptom:** Tests timeout after 2 minutes

**Solutions:**
1. Run tests sequentially:
   ```bash
   dotnet test --parallel none
   ```

2. Check system resources (CPU/Memory)

3. Close other applications

### Problem: gRPC connection failures

**Symptom:** `RpcException: Status(StatusCode="Unavailable")`

**Solutions:**
1. Ensure HTTP/2 is enabled (automatic in .NET 10)
2. Check firewall settings
3. Verify no antivirus is blocking local connections

### Problem: Test discovery issues

**Symptom:** No tests found

**Solution:**
```bash
# Rebuild the project
dotnet clean
dotnet build
dotnet test
```

---

## 📊 CI/CD Integration

### GitHub Actions

Add to `.github/workflows/test.yml`:
```yaml
- name: Run Integration Tests
  run: |
    cd tests/Sorcha.Peer.Service.Integration.Tests
    chmod +x run-integration-tests.sh
    ./run-integration-tests.sh --coverage
```

### Azure DevOps

Add to `azure-pipelines.yml`:
```yaml
- script: |
    dotnet test tests/Sorcha.Peer.Service.Integration.Tests/*.csproj \
      --logger trx \
      --collect:"XPlat Code Coverage"
  displayName: 'Run Integration Tests'
```

---

## 🎯 Next Steps

1. **Read the full documentation:** [README.md](README.md)
2. **Explore test code:** Start with [PeerDiscoveryTests.cs](PeerDiscoveryTests.cs)
3. **Add your own tests:** Follow examples in existing test classes
4. **Run specific categories:** Focus on areas you're working on
5. **Check coverage:** Ensure new code is tested

---

## 📚 Key Files

| File | Purpose |
|------|---------|
| `PeerDiscoveryTests.cs` | Peer registration and lookup tests |
| `PeerCommunicationTests.cs` | P2P messaging and streaming tests |
| `PeerThroughputTests.cs` | Performance and load tests |
| `PeerHealthTests.cs` | Health check and metrics tests |
| `Infrastructure/PeerTestFixture.cs` | Test fixture managing peer instances |
| `Infrastructure/PeerServiceFactory.cs` | WebApplicationFactory for test setup |
| `Infrastructure/TestHelpers.cs` | Utility functions |

---

## 💡 Tips

1. **Run frequently:** Execute tests before committing code
2. **Focus on failures:** Use `--filter` to isolate failing tests
3. **Check performance:** Watch for performance regressions in throughput tests
4. **Use watch mode:** `--watch` flag auto-runs tests on code changes
5. **Generate coverage:** Aim for >80% code coverage

---

## 🆘 Getting Help

- **Full documentation:** [README.md](README.md)
- **Report issues:** [GitHub Issues](https://github.com/SorchaProject/Sorcha/issues)
- **Architecture docs:** [docs/architecture.md](../../../docs/reference/architecture.md)
- **Peer Service docs:** [src/Apps/Services/Sorcha.Peer.Service/README.md](../../../src/Apps/Services/Sorcha.Peer.Service/README.md)

---

**Happy Testing! 🎉**
