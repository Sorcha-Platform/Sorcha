# Quickstart Guide: Validator Service Development

**Date**: 2025-12-22
**Feature**: Validator Service - Distributed Transaction Validation and Consensus
**Branch**: `002-validator-service`

## Overview

This guide helps developers quickly set up, build, test, and contribute to the Validator Service. It assumes familiarity with .NET 10, C# 13, and the Sorcha project structure.

---

## Prerequisites

### Required Software
- **.NET 10 SDK** (10.0.0 or later)
- **Visual Studio 2022** (17.13+) or **Visual Studio Code** with C# extension
- **Docker Desktop** (for Redis, Testcontainers)
- **Git** (for version control)

### Recommended Tools
- **Rider** (JetBrains IDE) - excellent for .NET development
- **Postman** or **Insomnia** - for REST API testing
- **BloomRPC** or **gRPCurl** - for gRPC testing
- **Redis Insight** - for inspecting memory pool data

### Required Knowledge
- **C# async/await patterns**
- **Dependency injection** (ASP.NET Core)
- **gRPC basics** (protocol buffers, service definitions)
- **Blockchain fundamentals** (hashing, consensus, forks)

---

## Quick Setup (5 Minutes)

### 1. Clone the Repository

```bash
git clone https://github.com/sorcha-platform/sorcha.git
cd Sorcha
git checkout 002-validator-service
```

### 2. Restore Dependencies

```bash
dotnet restore
```

### 3. Build the Solution

```bash
dotnet build
```

### 4. Run Tests

```bash
# Run all Validator Service tests
dotnet test tests/Sorcha.Validator.Service.Tests

# Run with coverage
dotnet test tests/Sorcha.Validator.Service.Tests --collect:"XPlat Code Coverage"
```

### 5. Run the Service (Standalone)

```bash
cd src/Services/Sorcha.Validator.Service
dotnet run
```

Service starts on:
- **HTTPS**: `https://localhost:7001`
- **HTTP**: `http://localhost:5001`
- **gRPC**: `https://localhost:7001` (same as HTTPS)

### 6. Verify Health

```bash
curl https://localhost:7001/health
```

Expected response:
```json
{
  "status": "Healthy",
  "validatorId": "...",
  "activeRegisters": 0,
  "lastHeartbeat": "2025-12-22T10:30:00Z",
  "issues": []
}
```

---

## Run with .NET Aspire (Recommended)

The Validator Service integrates with other Sorcha services via .NET Aspire orchestration.

### 1. Run Aspire AppHost

```bash
cd src/Apps/Sorcha.AppHost
dotnet run
```

### 2. Access Aspire Dashboard

Open browser to: `http://localhost:15888`

The dashboard shows:
- **All running services** (Validator, Peer, Wallet, Register, Blueprint)
- **Service logs** (real-time)
- **Metrics** (OpenTelemetry traces, metrics)
- **Resource graphs** (Redis, databases)

### 3. Test End-to-End Flow

From the Aspire dashboard:
1. **Submit Transaction** → Peer Service (`POST /api/v1/transactions`)
2. **Observe Validator** → Check logs for validation activity
3. **Build Docket** → Wait for time/size threshold
4. **Achieve Consensus** → Watch vote collection
5. **Persist Docket** → Verify in Register Service

---

## Project Structure Navigation

```text
Sorcha/
├── src/
│   ├── Services/
│   │   └── Sorcha.Validator.Service/        # ⭐ Main service project
│   │       ├── Program.cs                    # Entry point
│   │       ├── Endpoints/                    # REST API endpoints
│   │       ├── Services/                     # Business logic
│   │       ├── GrpcServices/                 # gRPC implementations
│   │       ├── Clients/                      # gRPC clients to other services
│   │       └── Models/                       # DTOs
│   │
│   ├── Common/
│   │   └── Sorcha.Validator.Core/            # ⭐ Pure validation logic
│   │       └── Validators/                   # Stateless validators
│   │
│   └── Apps/
│       └── Sorcha.AppHost/                   # Aspire orchestration
│
├── tests/
│   ├── Sorcha.Validator.Service.Tests/      # ⭐ Service tests
│   │   ├── Integration/                      # Integration tests
│   │   └── Unit/                             # Unit tests
│   │
│   └── Sorcha.Validator.Core.Tests/          # ⭐ Core library tests
│
└── specs/
    └── 002-validator-service/                # ⭐ This feature's docs
        ├── spec.md                           # Requirements
        ├── plan.md                           # Implementation plan
        ├── research.md                       # Technology decisions
        ├── data-model.md                     # Entity definitions
        └── contracts/                        # API contracts
```

---

## Common Development Tasks

### Task 1: Add a New Validation Rule

**File**: `src/Common/Sorcha.Validator.Core/Validators/TransactionValidator.cs`

```csharp
public class TransactionValidator
{
    public ValidationResult Validate(Transaction transaction)
    {
        var errors = new List<string>();

        // Add your validation rule here
        if (string.IsNullOrEmpty(transaction.BlueprintId))
            errors.Add("BlueprintId is required");

        return errors.Any()
            ? ValidationResult.Failure(errors)
            : ValidationResult.Success();
    }
}
```

**Test**: `tests/Sorcha.Validator.Core.Tests/TransactionValidatorTests.cs`

```csharp
[Fact]
public void Validate_WhenBlueprintIdMissing_ReturnsFailure()
{
    // Arrange
    var transaction = new Transaction { BlueprintId = null };
    var validator = new TransactionValidator();

    // Act
    var result = validator.Validate(transaction);

    // Assert
    result.IsValid.Should().BeFalse();
    result.Errors.Should().Contain("BlueprintId is required");
}
```

Run test:
```bash
dotnet test tests/Sorcha.Validator.Core.Tests --filter "Validate_WhenBlueprintIdMissing_ReturnsFailure"
```

---

### Task 2: Add a New gRPC Endpoint

**1. Update Protocol Buffer**

Edit `specs/002-validator-service/contracts/validator.proto`:

```protobuf
service ValidatorService {
  // ... existing methods

  // New method
  rpc GetValidatorInfo(GetValidatorInfoRequest) returns (GetValidatorInfoResponse);
}

message GetValidatorInfoRequest {
  string validator_id = 1;
}

message GetValidatorInfoResponse {
  string validator_id = 1;
  string system_wallet_address = 2;
  double reputation_score = 3;
}
```

**2. Generate C# Code**

```bash
# From project root
dotnet build src/Services/Sorcha.Validator.Service
# Protobuf compilation happens automatically via Grpc.Tools
```

**3. Implement Service**

Edit `src/Services/Sorcha.Validator.Service/GrpcServices/ValidatorGrpcService.cs`:

```csharp
public override async Task<GetValidatorInfoResponse> GetValidatorInfo(
    GetValidatorInfoRequest request,
    ServerCallContext context)
{
    var validator = await _validatorRepository.GetByIdAsync(request.ValidatorId);

    return new GetValidatorInfoResponse
    {
        ValidatorId = validator.ValidatorId,
        SystemWalletAddress = validator.SystemWalletAddress,
        ReputationScore = validator.ReputationScore
    };
}
```

**4. Test**

```bash
# Using gRPCurl
grpcurl -plaintext -d '{"validator_id":"123"}' \
  localhost:5001 sorcha.validator.v1.ValidatorService/GetValidatorInfo
```

---

### Task 3: Modify Memory Pool Behavior

**File**: `src/Services/Sorcha.Validator.Service/Services/MemPoolManager.cs`

```csharp
public class MemPoolManager : IMemPoolManager
{
    public async Task<bool> AddTransactionAsync(Transaction transaction)
    {
        // Custom logic: reject transactions older than 1 hour
        if (transaction.CreatedAt < DateTimeOffset.UtcNow.AddHours(-1))
        {
            _logger.LogWarning("Rejecting stale transaction {TxId}", transaction.TransactionId);
            return false;
        }

        // Add to appropriate priority queue
        return await AddToQueue(transaction);
    }
}
```

**Configuration**: `appsettings.json`

```json
{
  "MemPoolConfiguration": {
    "MaxSize": 10000,
    "DefaultTTL": "01:00:00",
    "HighPriorityQuota": 0.10,
    "CleanupInterval": "00:05:00"
  }
}
```

---

### Task 4: Debug Consensus Failures

**Enable Verbose Logging**:

Edit `appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Sorcha.Validator.Service.Services.ConsensusEngine": "Debug",
      "Sorcha.Validator.Service.Services.DocketBuilder": "Debug"
    }
  }
}
```

**Add Breakpoints**:
- `ConsensusEngine.CollectVotesAsync` - vote collection logic
- `DocketBuilder.BuildDocketAsync` - docket creation
- `ChainValidator.ResolveFork` - fork resolution

**Inspect State**:
```csharp
// In ConsensusEngine.cs
_logger.LogDebug("Collected votes: {VoteCount}/{TotalValidators}", votes.Count, validators.Count);
```

---

## Testing Strategies

### Unit Tests

**Location**: `tests/Sorcha.Validator.Service.Tests/Unit/`

**Example**: Test memory pool eviction policy

```csharp
[Fact]
public async Task AddTransaction_WhenPoolFull_EvictsOldest()
{
    // Arrange
    var config = new MemPoolConfiguration { MaxSize = 3 };
    var memPool = new MemPoolManager(config, _logger);

    var tx1 = CreateTransaction("tx1", priority: TransactionPriority.Normal);
    var tx2 = CreateTransaction("tx2", priority: TransactionPriority.Normal);
    var tx3 = CreateTransaction("tx3", priority: TransactionPriority.Normal);
    var tx4 = CreateTransaction("tx4", priority: TransactionPriority.Normal);

    await memPool.AddTransactionAsync(tx1);
    await memPool.AddTransactionAsync(tx2);
    await memPool.AddTransactionAsync(tx3);

    // Act - adding 4th transaction should evict tx1
    var added = await memPool.AddTransactionAsync(tx4);

    // Assert
    added.Should().BeTrue();
    memPool.CurrentSize.Should().Be(3);
    memPool.Contains(tx1.TransactionId).Should().BeFalse();
    memPool.Contains(tx4.TransactionId).Should().BeTrue();
}
```

### Integration Tests

**Location**: `tests/Sorcha.Validator.Service.Tests/Integration/`

**Example**: Test end-to-end docket creation

```csharp
[Fact]
public async Task DocketBuildingEndToEnd_Success()
{
    // Arrange - use Testcontainers for Redis
    await using var redisContainer = new RedisBuilder().Build();
    await redisContainer.StartAsync();

    var factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace Redis connection with test container
                services.AddSingleton<IConnectionMultiplexer>(
                    ConnectionMultiplexer.Connect(redisContainer.GetConnectionString()));
            });
        });

    var client = factory.CreateClient();

    // Act - start validator
    var startResponse = await client.PostAsJsonAsync("/api/v1/validators", new
    {
        registerId = "test-register",
        blueprintId = "test-blueprint"
    });

    startResponse.Should().HaveStatusCode(HttpStatusCode.OK);

    // Submit transactions via mock Peer Service
    // ... (simulate 50 transactions to trigger size threshold)

    // Wait for docket creation
    await Task.Delay(TimeSpan.FromSeconds(15));

    // Assert - check docket was created via Register Service mock
    // ...
}
```

### Performance Tests

**Tool**: NBomber (recommended) or BenchmarkDotNet

```csharp
[Fact]
public async Task TransactionValidation_MeetsP95Latency()
{
    // Arrange
    var validator = new TransactionValidator(_blueprintValidator, _cryptoService);
    var transactions = GenerateTransactions(1000);

    // Act - measure P95 latency
    var latencies = new List<TimeSpan>();
    foreach (var tx in transactions)
    {
        var sw = Stopwatch.StartNew();
        await validator.ValidateAsync(tx);
        sw.Stop();
        latencies.Add(sw.Elapsed);
    }

    // Assert - P95 < 500ms (NFR-001)
    var p95 = latencies.OrderBy(l => l.TotalMilliseconds).ElementAt((int)(latencies.Count * 0.95));
    p95.TotalMilliseconds.Should().BeLessThan(500);
}
```

---

## Configuration Reference

### appsettings.json

```json
{
  "ValidatorConfiguration": {
    "ValidatorId": "auto-generated-guid",
    "SystemWalletId": "configured-in-wallet-service",
    "MaxReorgDepth": 10
  },
  "ConsensusConfiguration": {
    "ApprovalThreshold": 0.51,
    "VoteTimeout": "00:00:30",
    "MaxRetries": 3,
    "RequireQuorum": true
  },
  "MemPoolConfiguration": {
    "MaxSize": 10000,
    "DefaultTTL": "01:00:00",
    "HighPriorityQuota": 0.10,
    "CleanupInterval": "00:05:00",
    "PersistenceBackend": "Redis"
  },
  "DocketBuildConfiguration": {
    "TimeThreshold": "00:00:10",
    "SizeThreshold": 50,
    "MaxTransactionsPerDocket": 100,
    "AllowEmptyDockets": false
  },
  "GrpcClients": {
    "WalletService": "https://localhost:7002",
    "PeerService": "https://localhost:7003",
    "RegisterService": "https://localhost:7004",
    "BlueprintService": "https://localhost:7005"
  }
}
```

---

## Troubleshooting

### Issue: Validator Not Receiving Transactions

**Symptoms**: Memory pool remains empty, no dockets created

**Diagnosis**:
1. Check Peer Service connectivity:
   ```bash
   grpcurl -plaintext localhost:7003 list
   ```
2. Verify Peer Service is routing transactions to Validator
3. Check Aspire dashboard logs for Peer Service

**Solution**: Ensure Peer Service has Validator's gRPC endpoint registered

---

### Issue: Consensus Always Fails

**Symptoms**: All proposed dockets rejected, consensus timeout errors

**Diagnosis**:
1. Check validator discovery - are peers reachable?
   ```bash
   curl https://localhost:7001/api/v1/metrics/consensus/test-register
   ```
2. Check `activeValidators` count - should be 2+
3. Verify validator system wallets are initialized

**Solution**:
- Run at least 2 validator instances
- Ensure Peer Service is providing validator list
- Check firewall rules for gRPC port (7001)

---

### Issue: Memory Pool Fills Up Quickly

**Symptoms**: High eviction count, transactions dropped

**Diagnosis**:
1. Check memory pool stats:
   ```bash
   curl https://localhost:7001/api/v1/metrics/mempool/test-register
   ```
2. Check docket build configuration - is `SizeThreshold` too high?
3. Check `TimeThreshold` - is it triggering frequently enough?

**Solution**:
- Lower `SizeThreshold` (e.g., from 50 to 25)
- Increase `MaxSize` if memory allows
- Adjust `TimeThreshold` for more frequent docket builds

---

## Next Steps

After familiarizing yourself with the Validator Service:

1. **Read the Specification** - [spec.md](./spec.md) - Understand requirements
2. **Review the Plan** - [plan.md](./plan.md) - See implementation strategy
3. **Check Tasks** - [tasks.md](./tasks.md) - Find work items (generated by `/speckit.tasks`)
4. **Join Discussions** - GitHub Discussions or team Slack
5. **Submit PRs** - Follow [CONTRIBUTING.md](../../../CONTRIBUTING.md)

---

## Resources

- **Sorcha Documentation**: [docs/](../../../docs/)
- **Constitution**: [.specify/memory/constitution.md](../../../.specify/memory/constitution.md)
- **gRPC Guide**: [https://grpc.io/docs/languages/csharp/](https://grpc.io/docs/languages/csharp/)
- **.NET Aspire**: [https://learn.microsoft.com/en-us/dotnet/aspire/](https://learn.microsoft.com/en-us/dotnet/aspire/)
- **Blockchain Basics**: [https://en.wikipedia.org/wiki/Blockchain](https://en.wikipedia.org/wiki/Blockchain)

---

**Happy Coding!** 🚀

If you have questions, reach out via GitHub Issues or the development team.
