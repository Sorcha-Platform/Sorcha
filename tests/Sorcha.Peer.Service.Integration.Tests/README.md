# Sorcha Peer Service - Integration Tests

⚠️ **STATUS: NOT COMPATIBLE - See [STATUS.md](STATUS.md) for details**

These integration tests are currently **incompatible** with the refactored Peer Service architecture. The tests were written for an older, simpler service structure and need to be completely rewritten.

**Do not attempt to run these tests** - they will fail. This test project has been removed from the solution until it can be updated.

---

## Original Documentation (For Reference)

Comprehensive integration test suite for the Sorcha Peer Service covering peer discovery, communication, and streaming throughput.

## Overview

This test suite validates peer-to-peer functionality across multiple service instances, testing:

- **Peer Discovery**: Registration, lookup, and management of peer nodes
- **P2P Communication**: Simple message exchange and bidirectional streaming
- **Throughput & Performance**: High-volume transaction processing and performance metrics
- **Health Monitoring**: Service health checks and metrics reporting

## Test Structure

```
Sorcha.Peer.Service.Integration.Tests/
├── Infrastructure/
│   ├── PeerServiceFactory.cs      # WebApplicationFactory for test instances
│   ├── PeerTestFixture.cs         # xUnit fixture managing multiple peers
│   └── TestHelpers.cs             # Utility functions for tests
├── PeerDiscoveryTests.cs          # Discovery & registration tests
├── PeerCommunicationTests.cs      # P2P messaging tests
├── PeerThroughputTests.cs         # Performance & load tests
├── PeerHealthTests.cs             # Health & metrics tests
└── README.md                      # This file
```

## Prerequisites

- .NET 10.0 SDK or later
- Visual Studio 2025 or Rider (optional)
- Port availability: Tests use random ports in the 5000-6000 range

## Running the Tests

### Command Line

#### Run all integration tests:
```bash
dotnet test
```

#### Run with verbose output:
```bash
dotnet test --logger "console;verbosity=detailed"
```

#### Run specific test class:
```bash
dotnet test --filter "FullyQualifiedName~PeerDiscoveryTests"
dotnet test --filter "FullyQualifiedName~PeerCommunicationTests"
dotnet test --filter "FullyQualifiedName~PeerThroughputTests"
```

#### Run specific test:
```bash
dotnet test --filter "FullyQualifiedName~RegisterPeer_Via_REST_Should_Return_Success"
```

#### Generate code coverage:
```bash
dotnet test --collect:"XPlat Code Coverage"
```

### Using Scripts

#### Windows (PowerShell):
```powershell
.\run-integration-tests.ps1
```

#### Linux/macOS (Bash):
```bash
./run-integration-tests.sh
```

### Visual Studio / Rider

1. Open Test Explorer
2. Right-click on `Sorcha.Peer.Service.Integration.Tests`
3. Select "Run" or "Debug"

## Test Categories

### 1. Peer Discovery Tests (`PeerDiscoveryTests.cs`)

Tests peer registration, discovery, and management via REST and gRPC endpoints.

**Key Tests:**
- `RegisterPeer_Via_REST_Should_Return_Success` - REST API peer registration
- `RegisterPeer_Via_gRPC_Should_Return_Success` - gRPC peer registration
- `GetAllPeers_Should_Return_Registered_Peers` - Peer list retrieval
- `Multiple_Peers_Can_Discover_Each_Other` - Cross-peer discovery
- `UnregisterPeer_Should_Remove_From_Registry` - Peer deregistration

**What it validates:**
- ✅ Peer registration with auto-generated IDs
- ✅ Peer metadata storage and retrieval
- ✅ REST and gRPC endpoint parity
- ✅ Peer lookup by ID
- ✅ Peer unregistration

### 2. Peer Communication Tests (`PeerCommunicationTests.cs`)

Tests bidirectional streaming communication between peers.

**Key Tests:**
- `Single_Transaction_Should_Be_Processed_Successfully` - Basic message exchange
- `Multiple_Sequential_Transactions_Should_Be_Processed` - Sequential messaging
- `Transactions_Between_Different_Peers_Should_Work` - Multi-peer communication
- `Large_Payload_Transaction_Should_Be_Handled` - Large message handling (1 MB)
- `Bidirectional_Stream_Should_Work_Correctly` - Full-duplex streaming
- `Concurrent_Streams_From_Same_Peer_Should_Work` - Multiple concurrent streams

**What it validates:**
- ✅ gRPC bidirectional streaming
- ✅ Transaction message routing
- ✅ Payload integrity
- ✅ Concurrent stream handling
- ✅ Metrics tracking during communication

### 3. Peer Throughput Tests (`PeerThroughputTests.cs`)

Performance and load testing for high-volume transaction processing.

**Key Tests:**
- `High_Volume_Transaction_Stream_Should_Maintain_Performance` - 1000 transactions/stream
- `Sustained_Load_Should_Not_Degrade_Performance` - Multiple batches over time
- `Large_Payload_Throughput_Test` - 10 KB payload performance
- `Parallel_Peer_Throughput_Test` - Multiple peers sending concurrently
- `Burst_Traffic_Should_Be_Handled_Gracefully` - Burst traffic patterns
- `Memory_Usage_Should_Remain_Stable_Under_Load` - Memory leak detection

**Performance Targets:**
- ✅ Minimum 100 transactions/second for small payloads
- ✅ Minimum 1 MB/second for large payloads
- ✅ Less than 30% performance degradation under sustained load
- ✅ Less than 50% memory growth over multiple iterations

**Console Output:**
These tests output detailed performance metrics:
```
Throughput: 234.56 transactions/second
Total time: 4.27 seconds
Average latency: 4.26 ms
Memory usage: 45.23 MB
```

### 4. Peer Health Tests (`PeerHealthTests.cs`)

Tests health check endpoints and metrics reporting.

**Key Tests:**
- `Health_Endpoint_Should_Return_Healthy_Status` - `/api/health` endpoint
- `Metrics_Endpoint_Should_Return_Current_Metrics` - `/api/metrics` endpoint
- `Metrics_Via_gRPC_Should_Match_REST_Metrics` - REST/gRPC consistency
- `Uptime_Should_Increase_Over_Time` - Uptime tracking
- `All_Peers_Should_Report_Healthy` - Multi-instance health

**What it validates:**
- ✅ Health check endpoint availability
- ✅ Metrics accuracy (transactions, throughput, uptime)
- ✅ REST and gRPC metrics consistency
- ✅ CPU and memory usage reporting

## Test Infrastructure

### PeerTestFixture

The `PeerTestFixture` class manages the lifecycle of multiple peer service instances:

```csharp
public class PeerTestFixture : IAsyncLifetime
{
    public List<PeerInstance> Peers { get; }  // 3 peers by default

    public async Task<PeerInstance> AddPeerInstanceAsync(string peerId);
}
```

**Features:**
- Creates isolated test instances with in-memory repositories
- Automatically assigns unique ports (5000-6000 range)
- Provides both HTTP and gRPC clients
- Handles graceful shutdown and cleanup

### PeerServiceFactory

Custom `WebApplicationFactory` that configures test instances:

```csharp
public class PeerServiceFactory : WebApplicationFactory<Program>
{
    public string PeerId { get; set; }
    public int Port { get; set; }
}
```

**Configuration:**
- Replaces Redis with in-memory output cache
- Uses `Testing` environment
- Disables Aspire service defaults for standalone testing
- Provides access to DI container for service inspection

### TestHelpers

Utility functions for common test operations:

```csharp
public static class TestHelpers
{
    public static byte[] CreateRandomPayload(int sizeInBytes);
    public static string GenerateTransactionId();
    public static Task<bool> WaitForConditionAsync(...);
    public static StringContent ToJsonContent<T>(this T obj);
    public static Task<T?> DeserializeAsync<T>(this HttpResponseMessage response);
}
```

## Writing New Tests

### Example: Adding a Discovery Test

```csharp
[Fact]
public async Task My_New_Discovery_Test()
{
    // Arrange
    var peer = _fixture.Peers[0];

    // Act
    var response = await peer.HttpClient.PostAsJsonAsync("/api/peers", new PeerNode
    {
        PeerId = "test-peer",
        Endpoint = "http://localhost:5555"
    });

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Created);
}
```

### Example: Adding a Communication Test

```csharp
[Fact]
public async Task My_New_Communication_Test()
{
    // Arrange
    var peer = _fixture.Peers[0];

    // Act
    using var call = peer.GrpcClient.StreamTransactions();
    await call.RequestStream.WriteAsync(new TransactionMessage
    {
        TransactionId = "test-txn",
        FromPeer = peer.PeerId,
        ToPeer = "destination",
        Payload = Google.Protobuf.ByteString.CopyFrom(TestHelpers.CreateRandomPayload(256)),
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    });
    await call.RequestStream.CompleteAsync();

    var response = await call.ResponseStream.ReadAllAsync().FirstAsync();

    // Assert
    response.Success.Should().BeTrue();
}
```

## Troubleshooting

### Port Conflicts

If tests fail with `AddressAlreadyInUseException`:
- Ensure ports 5000-6000 are available
- Check for orphaned test processes: `netstat -ano | findstr "5xxx"`
- Kill orphaned processes or restart your machine

### Timeout Errors

If tests timeout:
- Increase timeout in test: `await task.WaitAsync(TimeSpan.FromSeconds(30))`
- Check system resources (CPU, memory)
- Run tests sequentially: `dotnet test --parallel none`

### gRPC Connection Errors

If gRPC calls fail:
- Ensure `Grpc.AspNetCore` is version 2.71.0 or later
- Check that HTTP/2 is enabled (automatic in Kestrel)
- Verify Protocol Buffers are properly generated

### Memory Leaks

If memory usage grows unexpectedly:
- Run `Memory_Usage_Should_Remain_Stable_Under_Load` test
- Use dotMemory or PerfView for profiling
- Check for undisposed gRPC channels/streams

## CI/CD Integration

### GitHub Actions Example

```yaml
name: Integration Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - name: Run Integration Tests
        run: dotnet test tests/Sorcha.Peer.Service.Integration.Tests --logger "trx;LogFileName=test-results.trx"
      - name: Upload Test Results
        uses: actions/upload-artifact@v4
        if: always()
        with:
          name: test-results
          path: '**/test-results.trx'
```

### Azure DevOps Example

```yaml
- task: DotNetCoreCLI@2
  displayName: 'Run Integration Tests'
  inputs:
    command: 'test'
    projects: 'tests/Sorcha.Peer.Service.Integration.Tests/*.csproj'
    arguments: '--configuration Release --logger trx --collect:"XPlat Code Coverage"'
```

## Performance Benchmarks

Typical test execution times (on modern hardware):

| Test Suite | Tests | Duration | Notes |
|------------|-------|----------|-------|
| PeerDiscoveryTests | 12 | ~5s | Fast REST/gRPC calls |
| PeerCommunicationTests | 10 | ~10s | Streaming tests |
| PeerThroughputTests | 8 | ~30s | High-volume performance |
| PeerHealthTests | 6 | ~5s | Quick health checks |
| **Total** | **36** | **~50s** | Full suite |

## Additional Resources

- [ASP.NET Core Integration Tests](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests)
- [xUnit Documentation](https://xunit.net/)
- [FluentAssertions Documentation](https://fluentassertions.com/)
- [gRPC Testing Guide](https://grpc.io/docs/guides/testing/)
- [Sorcha Architecture Documentation](../../../docs/reference/architecture.md)

## Contributing

When adding new tests:

1. ✅ Follow existing naming conventions (`Should_Expected_Behavior`)
2. ✅ Use FluentAssertions for readable assertions
3. ✅ Add XML documentation comments for test classes
4. ✅ Group related tests in the same test class
5. ✅ Use `TestHelpers` for common operations
6. ✅ Include performance metrics in throughput tests
7. ✅ Update this README with new test categories

## License

```
SPDX-License-Identifier: MIT
Copyright (c) 2026 Sorcha Contributors
```
