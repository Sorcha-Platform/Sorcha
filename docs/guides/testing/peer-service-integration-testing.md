# Peer Service Integration Testing Guide

Complete guide for running, writing, and maintaining integration tests for the Sorcha Peer Service.

## Table of Contents

1. [Overview](#overview)
2. [Getting Started](#getting-started)
3. [Test Architecture](#test-architecture)
4. [Running Tests](#running-tests)
5. [Test Suites](#test-suites)
6. [Writing Tests](#writing-tests)
7. [Performance Testing](#performance-testing)
8. [Troubleshooting](#troubleshooting)
9. [CI/CD Integration](#cicd-integration)
10. [Best Practices](#best-practices)

---

## Overview

The Sorcha Peer Service integration test suite provides comprehensive end-to-end testing of peer-to-peer functionality using real service instances. Unlike unit tests that mock dependencies, these tests validate actual network communication, gRPC streaming, and multi-peer interactions.

### What We Test

- ✅ **Peer Discovery**: Registration, lookup, and peer list management
- ✅ **P2P Communication**: Bidirectional gRPC streaming between peers
- ✅ **Throughput**: High-volume transaction processing (1000+ tx/sec)
- ✅ **Performance**: Memory usage, latency, and sustained load
- ✅ **Health & Metrics**: Service health checks and metrics accuracy

### Test Statistics

- **Total Tests**: 36+
- **Test Categories**: 4 (Discovery, Communication, Throughput, Health)
- **Execution Time**: ~50 seconds (full suite)
- **Lines of Test Code**: 2000+

---

## Getting Started

### Prerequisites

1. **.NET 10.0 SDK** or later
2. **Ports 5000-6000** available (tests use random ports in this range)
3. **Visual Studio 2025**, **Rider**, or **VS Code** (optional)

### Quick Start

```bash
# Clone and navigate to test directory
cd tests/Sorcha.Peer.Service.Integration.Tests

# Run all tests
dotnet test

# Or use the helper script (Windows)
.\run-integration-tests.ps1

# Or use the helper script (Linux/macOS)
./run-integration-tests.sh
```

✅ **Expected**: All tests pass in ~50 seconds

---

## Test Architecture

### Design Pattern: WebApplicationFactory

We use ASP.NET Core's `WebApplicationFactory<T>` to create real, in-memory service instances:

```
┌─────────────────────────────────────────┐
│        PeerTestFixture (xUnit)          │
│  Manages lifecycle of 3+ peer instances │
└────────────┬────────────────────────────┘
             │
     ┌───────┴────────┬────────────┐
     │                │            │
┌────▼─────┐    ┌────▼─────┐  ┌──▼──────┐
│ Peer 1   │    │ Peer 2   │  │ Peer 3  │
│ HTTP+gRPC│    │ HTTP+gRPC│  │HTTP+gRPC│
└──────────┘    └──────────┘  └─────────┘
```

### Key Components

#### 1. PeerTestFixture
**Location**: `Infrastructure/PeerTestFixture.cs`

Manages multiple peer service instances:
```csharp
public class PeerTestFixture : IAsyncLifetime
{
    public List<PeerInstance> Peers { get; }  // 3 peers by default

    public async Task<PeerInstance> AddPeerInstanceAsync(string peerId);
    public async Task InitializeAsync();
    public async Task DisposeAsync();
}
```

**Features:**
- Creates isolated test instances
- Provides HTTP and gRPC clients for each peer
- Handles graceful startup and shutdown
- Shared across all tests in a collection

#### 2. PeerServiceFactory
**Location**: `Infrastructure/PeerServiceFactory.cs`

Custom `WebApplicationFactory` for service configuration:
```csharp
public class PeerServiceFactory : WebApplicationFactory<Program>
{
    public string PeerId { get; set; }
    public int Port { get; set; }
}
```

**Customizations:**
- Replaces Redis with in-memory cache
- Uses random ports to avoid conflicts
- Configures `Testing` environment
- Removes Aspire dependencies for standalone testing

#### 3. TestHelpers
**Location**: `Infrastructure/TestHelpers.cs`

Utility functions for common operations:
```csharp
public static class TestHelpers
{
    public static byte[] CreateRandomPayload(int sizeInBytes);
    public static string GenerateTransactionId();
    public static Task<bool> WaitForConditionAsync(...);
    public static StringContent ToJsonContent<T>(this T obj);
}
```

### Test Collection

All tests use the `[Collection("PeerIntegration")]` attribute to share the same `PeerTestFixture` instance, improving performance by reusing peer instances across tests.

---

## Running Tests

### Basic Commands

```bash
# Run all tests
dotnet test

# Run with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run specific test class
dotnet test --filter "FullyQualifiedName~PeerDiscoveryTests"

# Run specific test method
dotnet test --filter "RegisterPeer_Via_REST_Should_Return_Success"

# Run tests in parallel (default)
dotnet test --parallel

# Run tests sequentially
dotnet test --parallel none
```

### Using Helper Scripts

#### PowerShell (Windows)

```powershell
# Basic run
.\run-integration-tests.ps1

# Run with coverage
.\run-integration-tests.ps1 -Coverage

# Run specific suite
.\run-test-suite.ps1 discovery

# Run with verbose output
.\run-integration-tests.ps1 -Verbose

# Run in watch mode (auto-rerun on changes)
.\run-integration-tests.ps1 -Watch
```

#### Bash (Linux/macOS)

```bash
# Basic run
./run-integration-tests.sh

# Run with coverage
./run-integration-tests.sh --coverage

# Run with verbose output
./run-integration-tests.sh --verbose

# Disable parallel execution
./run-integration-tests.sh --no-parallel
```

### Visual Studio / Rider

1. Open **Test Explorer**
2. Right-click `Sorcha.Peer.Service.Integration.Tests`
3. Select **Run** or **Debug**
4. Use filters to run specific categories

---

## Test Suites

### 1. Peer Discovery Tests (`PeerDiscoveryTests.cs`)

**Purpose**: Validate peer registration, discovery, and management.

**Test Count**: 12 tests
**Execution Time**: ~5 seconds

#### Key Tests

| Test | Validates |
|------|-----------|
| `RegisterPeer_Via_REST_Should_Return_Success` | REST API registration |
| `RegisterPeer_Via_gRPC_Should_Return_Success` | gRPC registration |
| `RegisterPeer_Without_PeerId_Should_Generate_Id` | Auto-generated peer IDs |
| `GetAllPeers_Should_Return_Registered_Peers` | Peer listing |
| `Multiple_Peers_Can_Discover_Each_Other` | Cross-peer discovery |
| `UnregisterPeer_Should_Remove_From_Registry` | Peer cleanup |

#### Example Test

```csharp
[Fact]
public async Task RegisterPeer_Via_REST_Should_Return_Success()
{
    // Arrange
    var peer = _fixture.Peers[0];
    var newPeer = new PeerNode
    {
        PeerId = "test-peer",
        Endpoint = "http://localhost:5001",
        Metadata = new Dictionary<string, string> { ["region"] = "us-west" }
    };

    // Act
    var response = await peer.HttpClient.PostAsJsonAsync("/api/peers", newPeer);

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var registered = await response.Content.ReadFromJsonAsync<PeerNode>();
    registered!.PeerId.Should().Be("test-peer");
}
```

### 2. Peer Communication Tests (`PeerCommunicationTests.cs`)

**Purpose**: Validate peer-to-peer messaging and streaming.

**Test Count**: 10 tests
**Execution Time**: ~10 seconds

#### Key Tests

| Test | Validates |
|------|-----------|
| `Single_Transaction_Should_Be_Processed_Successfully` | Basic message exchange |
| `Multiple_Sequential_Transactions_Should_Be_Processed` | Sequential messaging |
| `Large_Payload_Transaction_Should_Be_Handled` | 1 MB message handling |
| `Bidirectional_Stream_Should_Work_Correctly` | Full-duplex streaming |
| `Concurrent_Streams_From_Same_Peer_Should_Work` | Multiple streams per peer |
| `Transaction_Processing_Should_Update_Metrics` | Metrics tracking |

#### Example Test

```csharp
[Fact]
public async Task Single_Transaction_Should_Be_Processed_Successfully()
{
    // Arrange
    var peer = _fixture.Peers[0];
    var transactionId = TestHelpers.GenerateTransactionId();

    // Act
    using var call = peer.GrpcClient.StreamTransactions();
    await call.RequestStream.WriteAsync(new TransactionMessage
    {
        TransactionId = transactionId,
        FromPeer = peer.PeerId,
        ToPeer = "destination",
        Payload = Google.Protobuf.ByteString.CopyFrom(
            TestHelpers.CreateRandomPayload(512)),
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    });
    await call.RequestStream.CompleteAsync();

    var response = await call.ResponseStream.ReadAllAsync().FirstAsync();

    // Assert
    response.Success.Should().BeTrue();
    response.TransactionId.Should().Be(transactionId);
}
```

### 3. Peer Throughput Tests (`PeerThroughputTests.cs`)

**Purpose**: Validate performance under load.

**Test Count**: 8 tests
**Execution Time**: ~30 seconds

#### Performance Targets

| Metric | Target | Test |
|--------|--------|------|
| Small payload throughput | >100 tx/sec | `High_Volume_Transaction_Stream_Should_Maintain_Performance` |
| Large payload throughput | >1 MB/sec | `Large_Payload_Throughput_Test` |
| Performance degradation | <30% | `Sustained_Load_Should_Not_Degrade_Performance` |
| Memory growth | <50% | `Memory_Usage_Should_Remain_Stable_Under_Load` |

#### Key Tests

| Test | Load Profile |
|------|-------------|
| `High_Volume_Transaction_Stream_Should_Maintain_Performance` | 1000 transactions @ 512 bytes |
| `Large_Payload_Throughput_Test` | 100 transactions @ 10 KB |
| `Parallel_Peer_Throughput_Test` | 3 peers × 200 transactions |
| `Burst_Traffic_Should_Be_Handled_Gracefully` | 3 bursts × 500 transactions |

#### Example Test Output

```
Throughput: 234.56 transactions/second
Total time: 4.27 seconds
Average latency: 4.26 ms
Memory usage: 45.23 MB
✓ System should handle at least 100 transactions per second
```

### 4. Peer Health Tests (`PeerHealthTests.cs`)

**Purpose**: Validate health checks and metrics reporting.

**Test Count**: 6 tests
**Execution Time**: ~5 seconds

#### Key Tests

| Test | Validates |
|------|-----------|
| `Health_Endpoint_Should_Return_Healthy_Status` | `/api/health` availability |
| `Metrics_Endpoint_Should_Return_Current_Metrics` | `/api/metrics` accuracy |
| `Metrics_Via_gRPC_Should_Match_REST_Metrics` | REST/gRPC consistency |
| `Uptime_Should_Increase_Over_Time` | Uptime tracking |

---

## Writing Tests

### Test Template

```csharp
[Fact]
public async Task MyTest_Should_Expected_Behavior()
{
    // Arrange - Set up test data and preconditions
    var peer = _fixture.Peers[0];
    var testData = CreateTestData();

    // Act - Execute the operation under test
    var result = await peer.GrpcClient.SomeOperation(testData);

    // Assert - Verify expected outcomes
    result.Should().NotBeNull();
    result.Success.Should().BeTrue();
}
```

### Best Practices

#### 1. Use Descriptive Test Names

✅ **Good:**
```csharp
RegisterPeer_Via_REST_Should_Return_Success
```

❌ **Bad:**
```csharp
Test1
RegisterPeerTest
```

#### 2. Use FluentAssertions

✅ **Good:**
```csharp
response.StatusCode.Should().Be(HttpStatusCode.OK);
peers.Should().HaveCountGreaterThan(0);
```

❌ **Bad:**
```csharp
Assert.Equal(HttpStatusCode.OK, response.StatusCode);
Assert.True(peers.Count > 0);
```

#### 3. Clean Up Resources

```csharp
[Fact]
public async Task My_Test()
{
    // Use 'using' for disposable resources
    using var call = peer.GrpcClient.StreamTransactions();

    // Test logic...

    // Explicit cleanup if needed
    await call.RequestStream.CompleteAsync();
}
```

#### 4. Isolate Test Data

```csharp
// ✅ Good - Unique IDs prevent conflicts
var peerId = $"test-{Guid.NewGuid():N}";

// ❌ Bad - Hardcoded IDs may conflict
var peerId = "test-peer";
```

#### 5. Test One Thing

✅ **Good:**
```csharp
[Fact]
public async Task RegisterPeer_Should_Return_Created_Status()
{
    var response = await peer.HttpClient.PostAsJsonAsync("/api/peers", newPeer);
    response.StatusCode.Should().Be(HttpStatusCode.Created);
}

[Fact]
public async Task RegisterPeer_Should_Return_Peer_With_Id()
{
    var response = await peer.HttpClient.PostAsJsonAsync("/api/peers", newPeer);
    var registered = await response.Content.ReadFromJsonAsync<PeerNode>();
    registered!.PeerId.Should().NotBeNullOrEmpty();
}
```

❌ **Bad:**
```csharp
[Fact]
public async Task RegisterPeer_Tests()
{
    // Testing multiple things in one test
    response.StatusCode.Should().Be(HttpStatusCode.Created);
    registered!.PeerId.Should().NotBeNullOrEmpty();
    registered.Endpoint.Should().NotBeNullOrEmpty();
    // ... etc
}
```

---

## Performance Testing

### Measuring Throughput

```csharp
var stopwatch = Stopwatch.StartNew();

// Send transactions
for (int i = 0; i < transactionCount; i++)
{
    await call.RequestStream.WriteAsync(transaction);
}

stopwatch.Stop();

var throughput = transactionCount / stopwatch.Elapsed.TotalSeconds;
Console.WriteLine($"Throughput: {throughput:F2} tx/sec");
```

### Performance Assertions

```csharp
// Minimum throughput
throughput.Should().BeGreaterThan(100,
    "System should handle at least 100 tx/sec");

// Maximum degradation
lastBatchThroughput.Should().BeGreaterThan(firstBatchThroughput * 0.7,
    "Performance should not degrade more than 30%");

// Memory stability
memoryGrowthPercent.Should().BeLessThan(50,
    "Memory usage should remain stable");
```

---

## Troubleshooting

### Common Issues

#### 1. Port Conflicts

**Symptom:**
```
System.Net.Sockets.SocketException: Address already in use
```

**Solution:**
```bash
# Windows
netstat -ano | findstr "5xxx"
taskkill /PID <pid> /F

# Linux/macOS
lsof -i :5xxx
kill -9 <pid>
```

#### 2. Test Timeouts

**Symptom:**
```
Test exceeded timeout of 120000ms
```

**Solutions:**
- Run sequentially: `dotnet test --parallel none`
- Increase timeout: `await task.WaitAsync(TimeSpan.FromMinutes(5))`
- Check system resources

#### 3. gRPC Errors

**Symptom:**
```
RpcException: Status(StatusCode="Unavailable")
```

**Solutions:**
- Ensure HTTP/2 is enabled (automatic in .NET 10)
- Check firewall/antivirus settings
- Verify Protocol Buffers are generated

#### 4. Random Test Failures

**Symptom:**
Tests pass individually but fail when run together

**Solutions:**
- Check for shared state between tests
- Ensure proper cleanup in `Dispose`
- Use unique test data IDs

---

## CI/CD Integration

### GitHub Actions

`.github/workflows/integration-tests.yml`:
```yaml
name: Integration Tests

on:
  push:
    branches: [master, develop]
  pull_request:
    branches: [master]

jobs:
  test:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore dependencies
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore

      - name: Run Integration Tests
        run: |
          cd tests/Sorcha.Peer.Service.Integration.Tests
          dotnet test --no-build --logger "trx;LogFileName=test-results.trx" \
            --collect:"XPlat Code Coverage"

      - name: Upload Test Results
        uses: actions/upload-artifact@v4
        if: always()
        with:
          name: test-results
          path: '**/test-results.trx'

      - name: Upload Coverage
        uses: codecov/codecov-action@v4
        with:
          files: '**/coverage.cobertura.xml'
```

### Azure DevOps

`azure-pipelines.yml`:
```yaml
trigger:
  branches:
    include:
      - master
      - develop

pool:
  vmImage: 'ubuntu-latest'

steps:
- task: UseDotNet@2
  inputs:
    version: '10.0.x'

- task: DotNetCoreCLI@2
  displayName: 'Restore'
  inputs:
    command: 'restore'

- task: DotNetCoreCLI@2
  displayName: 'Build'
  inputs:
    command: 'build'
    arguments: '--no-restore'

- task: DotNetCoreCLI@2
  displayName: 'Run Integration Tests'
  inputs:
    command: 'test'
    projects: 'tests/Sorcha.Peer.Service.Integration.Tests/*.csproj'
    arguments: '--no-build --logger trx --collect:"XPlat Code Coverage"'

- task: PublishTestResults@2
  condition: always()
  inputs:
    testResultsFormat: 'VSTest'
    testResultsFiles: '**/*.trx'

- task: PublishCodeCoverageResults@2
  inputs:
    summaryFileLocation: '$(Agent.TempDirectory)/**/coverage.cobertura.xml'
```

---

## Best Practices

### 1. Test Organization

- ✅ Group related tests in the same class
- ✅ Use `[Theory]` for parameterized tests
- ✅ Keep tests focused and independent
- ✅ Use descriptive test names

### 2. Performance Testing

- ✅ Output performance metrics to console
- ✅ Set realistic performance targets
- ✅ Test sustained load, not just bursts
- ✅ Monitor memory usage

### 3. Assertions

- ✅ Use FluentAssertions for readability
- ✅ Include descriptive failure messages
- ✅ Assert on specific values, not just "not null"
- ✅ Verify both success and error cases

### 4. Maintenance

- ✅ Keep tests fast (aim for <1 min total)
- ✅ Avoid flaky tests (timing-dependent logic)
- ✅ Update tests when APIs change
- ✅ Maintain documentation

### 5. Coverage

- ✅ Aim for >80% code coverage
- ✅ Cover happy paths and error cases
- ✅ Test edge cases and boundary conditions
- ✅ Test concurrent scenarios

---

## Additional Resources

- **Full Test Documentation**: [tests/Sorcha.Peer.Service.Integration.Tests/README.md](../../tests/Sorcha.Peer.Service.Integration.Tests/README.md)
- **Quick Start Guide**: [tests/Sorcha.Peer.Service.Integration.Tests/QUICKSTART.md](../../tests/Sorcha.Peer.Service.Integration.Tests/QUICKSTART.md)
- **Peer Service Documentation**: [src/Apps/Services/Sorcha.Peer.Service/README.md](../../src/Apps/Services/Sorcha.Peer.Service/README.md)
- **Architecture Overview**: [docs/architecture.md](../../reference/architecture.md)
- **ASP.NET Core Testing**: https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests
- **xUnit Documentation**: https://xunit.net/
- **FluentAssertions**: https://fluentassertions.com/

---

**Happy Testing! 🎉**
