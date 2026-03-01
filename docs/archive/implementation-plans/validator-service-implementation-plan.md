# Sorcha Validator Service - Implementation Plan

## Document Information

| Field | Value |
|-------|-------|
| **Service** | Sorcha Validator Service |
| **Version** | 1.0 |
| **Status** | Implementation Plan |
| **Date** | 2025-11-16 |
| **Author** | Claude Code (Anthropic) |
| **Related Docs** | `/docs/validator-service-design.md`, `/.specify/specs/sorcha-validator-service.md` |

---

## Table of Contents

1. [Overview](#1-overview)
2. [Project Phases](#2-project-phases)
3. [Phase 1: Foundation (Sorcha.Validator.Core)](#3-phase-1-foundation-sorchavalidatorcore)
4. [Phase 2: Service Infrastructure](#4-phase-2-service-infrastructure)
5. [Phase 3: Validation Components](#5-phase-3-validation-components)
6. [Phase 4: Consensus & Coordination](#6-phase-4-consensus--coordination)
7. [Phase 5: Integration & Testing](#7-phase-5-integration--testing)
8. [Phase 6: Production Readiness](#8-phase-6-production-readiness)
9. [Risk Management](#9-risk-management)
10. [Dependencies](#10-dependencies)
11. [Timeline](#11-timeline)

---

## 1. Overview

### 1.1 Project Scope

Implement the Sorcha Validator Service, a blockchain consensus and validation component responsible for:
- Building and validating Dockets (blocks)
- Managing Transaction MemPools
- Coordinating distributed consensus
- Creating genesis blocks for Registers
- Supporting secure enclave execution

### 1.2 Success Criteria

**Functional:**
- ✅ Build Dockets from MemPool with < 5s for 100 Transactions
- ✅ Validate Dockets with < 2s for 100 Transactions
- ✅ Achieve consensus with < 30s for 3 validators
- ✅ Create genesis blocks for new Registers
- ✅ Support enclave execution (at least one platform)

**Quality:**
- ✅ 90%+ test coverage for Sorcha.Validator.Core
- ✅ 70%+ test coverage for Sorcha.Validator.Service
- ✅ All security requirements met
- ✅ All integration tests passing

**Operational:**
- ✅ Service deploys via .NET Aspire
- ✅ Health checks operational
- ✅ Metrics exposed and queryable
- ✅ API documentation complete

### 1.3 Assumptions

- Wallet Service is implemented and available for signature verification
- Peer Service is implemented and available for Docket broadcasting
- Register Service is implemented and available for blockchain storage
- Blueprint Service is implemented and available for schema retrieval
- Redis is available for distributed caching (optional)

---

## 2. Project Phases

| Phase | Focus | Duration | Deliverables |
|-------|-------|----------|--------------|
| **Phase 1** | Foundation (Core Library) | 2 weeks | Sorcha.Validator.Core with validators |
| **Phase 2** | Service Infrastructure | 1 week | API endpoints, middleware, DI setup |
| **Phase 3** | Validation Components | 2 weeks | DocketBuilder, TransactionValidator, MemPoolManager |
| **Phase 4** | Consensus & Coordination | 2 weeks | ConsensusEngine, ValidatorOrchestrator, GenesisManager |
| **Phase 5** | Integration & Testing | 2 weeks | Integration tests, external service integration |
| **Phase 6** | Production Readiness | 1 week | Documentation, deployment, monitoring |

**Total Estimated Duration:** 10 weeks

---

## 3. Phase 1: Foundation (Sorcha.Validator.Core)

**Objective:** Build enclave-safe validation library with pure logic.

**Duration:** 2 weeks

### 3.1 Tasks

#### Task 1.1: Project Setup
**Estimated Effort:** 2 hours

**Acceptance Criteria:**
- ✅ `src/Common/Sorcha.Validator.Core/Sorcha.Validator.Core.csproj` created
- ✅ Target framework: `net10.0`
- ✅ Package references: `System.Text.Json`, `JsonSchema.Net`, `System.Security.Cryptography`
- ✅ No ASP.NET dependencies
- ✅ Test project `tests/Sorcha.Validator.Core.Tests/` created

**Implementation Steps:**
```bash
# Create core library
dotnet new classlib -n Sorcha.Validator.Core -o src/Common/Sorcha.Validator.Core -f net10.0

# Add to solution
dotnet sln add src/Common/Sorcha.Validator.Core/Sorcha.Validator.Core.csproj

# Create test project
dotnet new xunit -n Sorcha.Validator.Core.Tests -o tests/Sorcha.Validator.Core.Tests -f net10.0
dotnet sln add tests/Sorcha.Validator.Core.Tests/Sorcha.Validator.Core.Tests.csproj

# Add project references
dotnet add tests/Sorcha.Validator.Core.Tests reference src/Common/Sorcha.Validator.Core

# Add test packages
dotnet add tests/Sorcha.Validator.Core.Tests package FluentAssertions
dotnet add tests/Sorcha.Validator.Core.Tests package Moq
```

#### Task 1.2: Data Models
**Estimated Effort:** 4 hours

**Deliverables:**
- `Models/ValidationResult.cs`
- `Models/ValidationError.cs`
- `Models/ValidationRules.cs`
- `Models/ValidationSeverity.cs` (enum)

**Acceptance Criteria:**
- ✅ All models are records (immutable)
- ✅ XML documentation comments
- ✅ DataAnnotations for validation

**Example:**
```csharp
// Models/ValidationResult.cs
namespace Sorcha.Validator.Core.Models;

public record ValidationResult
{
    public bool IsValid { get; init; }
    public List<ValidationError> Errors { get; init; } = [];

    public static ValidationResult Success() => new() { IsValid = true };

    public static ValidationResult Failed(params ValidationError[] errors) => new()
    {
        IsValid = false,
        Errors = errors.ToList()
    };

    public static ValidationResult Failed(params string[] messages) => new()
    {
        IsValid = false,
        Errors = messages.Select(m => new ValidationError
        {
            Code = "VALIDATION_FAILED",
            Message = m,
            Severity = ValidationSeverity.Error
        }).ToList()
    };
}
```

#### Task 1.3: DocketValidator
**Estimated Effort:** 8 hours

**Deliverables:**
- `Validators/DocketValidator.cs`
- `Validators/DocketValidatorTests.cs`

**Key Methods:**
- `ValidateDocket(Docket docket, Docket? previousDocket, ValidationRules rules)`
- `ComputeDocketHash(Docket docket)`
- `ValidateChainIntegrity(Docket docket, Docket previousDocket)`
- `ValidateGenesisBlock(Docket docket)`

**Acceptance Criteria:**
- ✅ Stateless, pure functions
- ✅ No I/O operations
- ✅ Deterministic hash computation (SHA256)
- ✅ Chain integrity validation (PreviousHash linkage)
- ✅ Genesis block validation (DocketNumber = 0, PreviousHash = zeros)
- ✅ Timestamp validation (must be after previous Docket)
- ✅ Test coverage > 95%

**Test Scenarios:**
- ✅ Valid genesis block passes
- ✅ Invalid hash fails
- ✅ PreviousHash mismatch fails
- ✅ DocketNumber sequence invalid fails
- ✅ Timestamp before previous Docket fails
- ✅ Hash computation is deterministic

#### Task 1.4: TransactionValidator
**Estimated Effort:** 8 hours

**Deliverables:**
- `Validators/TransactionValidator.cs`
- `Validators/TransactionValidatorTests.cs`

**Key Methods:**
- `ValidateTransaction(Transaction tx, Blueprint blueprint, Action action, string? previousTxHash)`
- `ValidateAgainstSchemas(JsonNode payload, IEnumerable<JsonDocument> schemas)`
- `ValidateStructure(Transaction tx)`

**Acceptance Criteria:**
- ✅ Basic structure validation (TxId, RegisterId, etc.)
- ✅ Blueprint/Action reference validation
- ✅ JSON Schema validation (using JsonSchema.Net)
- ✅ Previous transaction reference validation (if chained)
- ✅ Test coverage > 95%

**Test Scenarios:**
- ✅ Valid Transaction passes
- ✅ Missing TxId fails
- ✅ Blueprint ID mismatch fails
- ✅ Schema validation failures
- ✅ Previous transaction reference invalid fails

#### Task 1.5: ConsensusValidator
**Estimated Effort:** 6 hours

**Deliverables:**
- `Validators/ConsensusValidator.cs`
- `Validators/ConsensusValidatorTests.cs`

**Key Methods:**
- `ValidateConsensusVote(ConsensusVote vote, string docketHash, string validatorAddress, byte[] publicKey)`
- `ValidateQuorum(IEnumerable<ConsensusVote> votes, ConsensusConfiguration config)`

**Acceptance Criteria:**
- ✅ Vote structure validation
- ✅ Signature verification (using provided public key)
- ✅ Quorum calculation (percentage-based)
- ✅ Minimum validator check
- ✅ Test coverage > 95%

**Test Scenarios:**
- ✅ Valid vote with valid signature passes
- ✅ Invalid signature fails
- ✅ DocketHash mismatch fails
- ✅ Quorum achieved with 2-of-3 validators
- ✅ Quorum failed with 1-of-3 validators

#### Task 1.6: ChainValidator
**Estimated Effort:** 6 hours

**Deliverables:**
- `Validators/ChainValidator.cs`
- `Validators/ChainValidatorTests.cs`

**Key Methods:**
- `ValidateChainSegment(IEnumerable<Docket> dockets)`
- `DetectFork(Docket docket1, Docket docket2)`
- `ValidateChainContinuity(Docket current, Docket previous)`

**Acceptance Criteria:**
- ✅ Chain segment validation (continuous DocketNumbers, PreviousHash linkage)
- ✅ Fork detection (two Dockets with same DocketNumber, different hashes)
- ✅ Test coverage > 95%

#### Task 1.7: HashingUtilities
**Estimated Effort:** 4 hours

**Deliverables:**
- `Cryptography/HashingUtilities.cs`
- `Cryptography/HashingUtilitiesTests.cs`

**Key Methods:**
- `ComputeSHA256Hash(string input)`
- `ComputeSHA256Hash(byte[] input)`
- `VerifyHash(string input, string expectedHash)`

**Acceptance Criteria:**
- ✅ SHA256 hashing implementation
- ✅ Lowercase hex string output
- ✅ Deterministic (same input = same output)
- ✅ Test coverage > 95%

### 3.2 Phase 1 Deliverables

**Code:**
- ✅ `Sorcha.Validator.Core.csproj` (library)
- ✅ 7 validator classes with full implementation
- ✅ 7 test classes with 95%+ coverage
- ✅ All models and utilities

**Documentation:**
- ✅ XML documentation comments on all public members
- ✅ README.md in Sorcha.Validator.Core explaining usage

**Quality Gates:**
- ✅ All tests passing
- ✅ Test coverage > 90%
- ✅ No compiler warnings
- ✅ Static analysis passing (dotnet format, Roslyn analyzers)

---

## 4. Phase 2: Service Infrastructure

**Objective:** Set up ASP.NET Core service with Minimal APIs, middleware, and configuration.

**Duration:** 1 week

### 4.1 Tasks

#### Task 2.1: Project Setup
**Estimated Effort:** 2 hours

**Acceptance Criteria:**
- ✅ `src/Services/Sorcha.Validator.Service/Sorcha.Validator.Service.csproj` created
- ✅ Target framework: `net10.0`
- ✅ Project references: `Sorcha.Validator.Core`, `Sorcha.ServiceDefaults`, `Sorcha.Blueprint.Models`
- ✅ Package references: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`
- ✅ Test project `tests/Sorcha.Validator.Service.Tests/` created
- ✅ Added to Sorcha.sln
- ✅ Added to AppHost.cs orchestration

**Implementation Steps:**
```bash
# Create service
dotnet new webapi -n Sorcha.Validator.Service -o src/Services/Sorcha.Validator.Service -minimal -f net10.0

# Add to solution
dotnet sln add src/Services/Sorcha.Validator.Service/Sorcha.Validator.Service.csproj

# Add project references
dotnet add src/Services/Sorcha.Validator.Service reference src/Common/Sorcha.Validator.Core
dotnet add src/Services/Sorcha.Validator.Service reference src/Common/Sorcha.ServiceDefaults
dotnet add src/Services/Sorcha.Validator.Service reference src/Common/Sorcha.Blueprint.Models

# Create test project
dotnet new xunit -n Sorcha.Validator.Service.Tests -o tests/Sorcha.Validator.Service.Tests -f net10.0
dotnet sln add tests/Sorcha.Validator.Service.Tests/Sorcha.Validator.Service.Tests.csproj
dotnet add tests/Sorcha.Validator.Service.Tests package Microsoft.AspNetCore.Mvc.Testing
```

#### Task 2.2: Program.cs Setup
**Estimated Effort:** 4 hours

**Deliverables:**
- `Program.cs` with Minimal API setup
- `appsettings.json` with configuration
- `appsettings.Development.json`

**Acceptance Criteria:**
- ✅ Calls `builder.AddServiceDefaults()`
- ✅ OpenAPI configured with Scalar UI
- ✅ Health checks mapped
- ✅ Configuration sections registered
- ✅ DI container configured (empty services initially)

**Example:**
```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add service defaults (health checks, telemetry, service discovery)
builder.AddServiceDefaults();

// Add OpenAPI
builder.Services.AddOpenApi();

// Configure settings
builder.Services.Configure<ValidatorServiceConfiguration>(
    builder.Configuration.GetSection("ValidatorService"));

// Add services (to be implemented in Phase 3)
// builder.Services.AddScoped<IValidatorOrchestrator, ValidatorOrchestrator>();
// ...

var app = builder.Build();

// Map default endpoints (health checks)
app.MapDefaultEndpoints();

// Map OpenAPI
app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("Validator Service API")
               .WithTheme(ScalarTheme.Purple)
               .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

// Map endpoint groups (to be implemented in Phase 2)
var validationGroup = app.MapGroup("/api/validation")
    .WithTags("Validation")
    .WithOpenApi();

var adminGroup = app.MapGroup("/api/admin/validation")
    .WithTags("Admin")
    .WithOpenApi();

var metricsGroup = app.MapGroup("/api/metrics")
    .WithTags("Metrics")
    .WithOpenApi();

app.Run();

// Make Program class accessible for tests
public partial class Program { }
```

#### Task 2.3: Configuration Models
**Estimated Effort:** 3 hours

**Deliverables:**
- `Configuration/ValidatorServiceConfiguration.cs`
- `Configuration/ConsensusConfiguration.cs`
- `Configuration/SecurityConfiguration.cs`

**Acceptance Criteria:**
- ✅ All configuration properties with defaults
- ✅ DataAnnotations for validation
- ✅ XML documentation comments

#### Task 2.4: Data Models (Service Layer)
**Estimated Effort:** 4 hours

**Deliverables:**
- `Models/Docket.cs`
- `Models/Transaction.cs`
- `Models/ConsensusVote.cs`
- `Models/GenesisConfig.cs`
- `Models/MemPoolStats.cs`
- `Models/ValidationStatus.cs`

**Acceptance Criteria:**
- ✅ All models with DataAnnotations
- ✅ JSON serialization configured
- ✅ XML documentation

#### Task 2.5: Middleware
**Estimated Effort:** 4 hours

**Deliverables:**
- `Middleware/ErrorHandlingMiddleware.cs`
- Rate limiting configured in Program.cs

**Acceptance Criteria:**
- ✅ Global error handling with ProblemDetails
- ✅ Rate limiting per endpoint
- ✅ Structured error logging

#### Task 2.6: AppHost Integration
**Estimated Effort:** 2 hours

**Deliverables:**
- Updated `src/Apps/Sorcha.AppHost/AppHost.cs`

**Acceptance Criteria:**
- ✅ Validator Service added to orchestration
- ✅ Dependencies on Wallet, Peer, Register, Blueprint services
- ✅ Redis reference
- ✅ Service starts successfully in Aspire

**Example:**
```csharp
// In AppHost.cs
var validatorService = builder.AddProject<Projects.Sorcha_Validator_Service>("validator-service")
    .WithReference(redis)
    .WithReference(walletService)
    .WithReference(peerService)
    .WithReference(registerService)
    .WithReference(blueprintService);

var apiGateway = builder.AddProject<Projects.Sorcha_ApiGateway>("api-gateway")
    .WithReference(validatorService)
    .WithExternalHttpEndpoints();
```

### 4.2 Phase 2 Deliverables

**Code:**
- ✅ Service project structure complete
- ✅ Program.cs with minimal API setup
- ✅ Configuration models
- ✅ Data models
- ✅ Middleware
- ✅ AppHost integration

**Quality Gates:**
- ✅ Service starts successfully
- ✅ `/health` endpoint returns 200 OK
- ✅ `/openapi/v1.json` returns OpenAPI spec
- ✅ Scalar UI accessible at `/scalar/v1`

---

## 5. Phase 3: Validation Components

**Objective:** Implement DocketBuilder, TransactionValidator, and MemPoolManager.

**Duration:** 2 weeks

### 5.1 Tasks

#### Task 3.1: External Service Clients
**Estimated Effort:** 8 hours

**Deliverables:**
- `Clients/WalletServiceClient.cs`
- `Clients/PeerServiceClient.cs`
- `Clients/RegisterServiceClient.cs`
- `Clients/BlueprintServiceClient.cs`

**Acceptance Criteria:**
- ✅ All clients use `HttpClient` via DI
- ✅ Service discovery integration
- ✅ Resilience handlers (retry, circuit breaker)
- ✅ Structured logging
- ✅ Unit tests with mock HttpClient

**Example:**
```csharp
// Clients/WalletServiceClient.cs
public class WalletServiceClient(HttpClient httpClient, ILogger<WalletServiceClient> logger)
{
    public async Task<bool> VerifySignatureAsync(
        string data,
        string signature,
        string address,
        CancellationToken ct = default)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync(
                "/api/signatures/verify",
                new { data, signature, address },
                ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Signature verification failed: {StatusCode}", response.StatusCode);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<VerificationResult>(ct);
            return result?.IsValid ?? false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error verifying signature");
            return false;
        }
    }
}

// In Program.cs
builder.Services.AddHttpClient<WalletServiceClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ValidatorService:WalletServiceUrl"]!);
})
.AddStandardResilienceHandler()
.AddServiceDiscovery();
```

#### Task 3.2: MemPoolManager
**Estimated Effort:** 12 hours

**Deliverables:**
- `Services/IMemPoolManager.cs`
- `Services/MemPoolManager.cs`
- `Services/MemPoolManagerTests.cs`

**Acceptance Criteria:**
- ✅ Thread-safe per-Register MemPools
- ✅ Size limits enforced (MaxMemPoolSizePerRegister)
- ✅ Transaction expiration (based on TransactionExpirationTime)
- ✅ FIFO ordering (oldest Transactions first)
- ✅ Background cleanup task
- ✅ Statistics tracking (MemPoolStats)
- ✅ Test coverage > 80%

**Implementation:**
```csharp
public class MemPoolManager : IMemPoolManager
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Transaction>> _memPools = new();
    private readonly ILogger<MemPoolManager> _logger;
    private readonly IOptions<ValidatorServiceConfiguration> _config;

    public async Task<bool> AddTransactionAsync(
        string registerId,
        Transaction transaction,
        CancellationToken ct = default)
    {
        var memPool = _memPools.GetOrAdd(registerId, _ => new ConcurrentQueue<Transaction>());

        // Check size limit
        if (memPool.Count >= _config.Value.MaxMemPoolSizePerRegister)
        {
            _logger.LogWarning("MemPool full for Register {RegisterId}", registerId);
            return false;
        }

        // Add to queue
        memPool.Enqueue(transaction);
        _logger.LogDebug("Transaction {TxId} added to MemPool for Register {RegisterId}",
            transaction.TxId, registerId);

        return true;
    }

    // ... other methods
}
```

#### Task 3.3: TransactionValidator (Service Layer)
**Estimated Effort:** 12 hours

**Deliverables:**
- `Services/ITransactionValidator.cs`
- `Services/TransactionValidator.cs`
- `Services/TransactionValidatorTests.cs`

**Acceptance Criteria:**
- ✅ Uses `Sorcha.Validator.Core.TransactionValidator` for pure validation
- ✅ Integrates with `WalletServiceClient` for signature verification
- ✅ Integrates with `BlueprintServiceClient` for schema retrieval
- ✅ Caches Blueprint/Action schemas (Redis or in-memory)
- ✅ Batch validation support
- ✅ Test coverage > 80%

**Implementation:**
```csharp
public class TransactionValidator(
    WalletServiceClient walletClient,
    BlueprintServiceClient blueprintClient,
    ILogger<TransactionValidator> logger) : ITransactionValidator
{
    public async Task<TransactionValidationResult> ValidateAsync(
        Transaction transaction,
        ValidationContext context,
        CancellationToken ct = default)
    {
        // 1. Basic structure validation (using Core library)
        var structureResult = Core.Validators.TransactionValidator.ValidateStructure(transaction);
        if (!structureResult.IsValid)
        {
            return TransactionValidationResult.Invalid(transaction.TxId, structureResult.Errors.ToArray());
        }

        // 2. Get Blueprint and Action
        var blueprint = context.Blueprint ?? await blueprintClient.GetBlueprintAsync(transaction.BlueprintId, ct);
        if (blueprint is null)
        {
            return TransactionValidationResult.Invalid(transaction.TxId, new ValidationError
            {
                Code = "BLUEPRINT_NOT_FOUND",
                Message = $"Blueprint {transaction.BlueprintId} not found"
            });
        }

        var action = blueprint.Actions.FirstOrDefault(a => a.Id == transaction.ActionId);
        if (action is null)
        {
            return TransactionValidationResult.Invalid(transaction.TxId, new ValidationError
            {
                Code = "ACTION_NOT_FOUND",
                Message = $"Action {transaction.ActionId} not found in Blueprint"
            });
        }

        // 3. Validate payload against schema (using Core library)
        var schemaResult = Core.Validators.TransactionValidator.ValidateTransaction(
            transaction, blueprint, action, context.PreviousTxHash);
        if (!schemaResult.IsValid)
        {
            return TransactionValidationResult.Invalid(transaction.TxId, schemaResult.Errors.ToArray());
        }

        // 4. Verify signature (external service call)
        if (context.RequireSignatureVerification)
        {
            var signatureValid = await walletClient.VerifySignatureAsync(
                SerializeTransactionForSigning(transaction),
                transaction.Signature,
                transaction.SenderAddress,
                ct);

            if (!signatureValid)
            {
                return TransactionValidationResult.Invalid(transaction.TxId, new ValidationError
                {
                    Code = "SIGNATURE_INVALID",
                    Message = "Transaction signature verification failed",
                    Severity = ValidationSeverity.Critical
                });
            }
        }

        return TransactionValidationResult.Valid(transaction.TxId);
    }
}
```

#### Task 3.4: DocketBuilder
**Estimated Effort:** 16 hours

**Deliverables:**
- `Services/IDocketBuilder.cs`
- `Services/DocketBuilder.cs`
- `Services/DocketBuilderTests.cs`

**Acceptance Criteria:**
- ✅ Builds Dockets from MemPool Transactions
- ✅ Validates each Transaction before inclusion
- ✅ Enforces size limits (MaxTransactionsPerDocket, MaxDocketSizeBytes)
- ✅ Computes Docket hash using `Sorcha.Validator.Core.DocketValidator`
- ✅ Retrieves previous Docket from Register Service
- ✅ Handles genesis blocks
- ✅ Metrics tracking (DocketBuildMetrics)
- ✅ Test coverage > 80%

**Implementation:**
```csharp
public class DocketBuilder(
    IMemPoolManager memPoolManager,
    ITransactionValidator transactionValidator,
    RegisterServiceClient registerClient,
    ILogger<DocketBuilder> logger) : IDocketBuilder
{
    public async Task<DocketBuildResult> BuildDocketAsync(
        string registerId,
        string validatorWalletAddress,
        CancellationToken ct = default)
    {
        var startTime = DateTimeOffset.UtcNow;
        var metrics = new DocketBuildMetrics();

        // 1. Get pending transactions
        var pendingTransactions = await memPoolManager.GetPendingTransactionsAsync(
            registerId,
            _config.Value.MaxTransactionsPerDocket,
            ct);

        if (!pendingTransactions.Any())
        {
            return DocketBuildResult.Failed("No pending transactions in MemPool");
        }

        metrics.TransactionsProcessed = pendingTransactions.Count();

        // 2. Validate each transaction
        var validTransactions = new List<Transaction>();
        var rejectedCount = 0;

        foreach (var tx in pendingTransactions)
        {
            var validationResult = await transactionValidator.ValidateAsync(tx, new ValidationContext
            {
                RegisterId = registerId,
                RequireSignatureVerification = true
            }, ct);

            if (validationResult.IsValid)
            {
                validTransactions.Add(tx);
            }
            else
            {
                rejectedCount++;
                logger.LogWarning("Transaction {TxId} rejected: {Errors}",
                    tx.TxId, string.Join(", ", validationResult.Errors.Select(e => e.Message)));
            }
        }

        if (!validTransactions.Any())
        {
            return DocketBuildResult.Failed("No valid transactions to include in Docket");
        }

        metrics.TransactionsIncluded = validTransactions.Count;
        metrics.TransactionsRejected = rejectedCount;

        // 3. Get previous docket
        var latestDocketNumber = await registerClient.GetLatestDocketNumberAsync(registerId, ct);
        Docket? previousDocket = null;
        if (latestDocketNumber > 0)
        {
            previousDocket = await registerClient.GetDocketAsync(registerId, latestDocketNumber, ct);
        }

        // 4. Create new docket
        var docket = new Docket
        {
            RegisterId = registerId,
            DocketNumber = previousDocket?.DocketNumber + 1 ?? 1,
            PreviousHash = previousDocket?.Hash ?? "0000000000000000000000000000000000000000000000000000000000000000",
            Timestamp = DateTimeOffset.UtcNow,
            ValidatorAddress = validatorWalletAddress,
            Transactions = validTransactions,
            IsGenesis = false
        };

        // 5. Compute hash
        docket.Hash = Core.Validators.DocketValidator.ComputeDocketHash(docket);

        // 6. Validate docket integrity
        var docketValidation = Core.Validators.DocketValidator.ValidateDocket(docket, previousDocket, new ValidationRules());
        if (!docketValidation.IsValid)
        {
            return DocketBuildResult.Failed(docketValidation.Errors.Select(e => e.Message).ToArray());
        }

        metrics.BuildDuration = DateTimeOffset.UtcNow - startTime;
        metrics.DocketSizeBytes = docket.Size;

        logger.LogInformation(
            "Docket built: RegisterId={RegisterId}, DocketNumber={DocketNumber}, " +
            "Transactions={TransactionCount}, Duration={Duration}ms",
            docket.RegisterId, docket.DocketNumber, docket.Transactions.Count, metrics.BuildDuration.TotalMilliseconds);

        return DocketBuildResult.Success(docket, metrics);
    }
}
```

#### Task 3.5: Validation Endpoints
**Estimated Effort:** 8 hours

**Deliverables:**
- `Endpoints/ValidationEndpoints.cs`

**Acceptance Criteria:**
- ✅ POST /api/validation/dockets/build
- ✅ POST /api/validation/dockets/validate
- ✅ POST /api/validation/transactions/add
- ✅ GET /api/validation/transactions/{registerId}
- ✅ All endpoints with OpenAPI documentation
- ✅ Integration tests for all endpoints

**Example:**
```csharp
// Endpoints/ValidationEndpoints.cs
public static class ValidationEndpoints
{
    public static RouteGroupBuilder MapValidationEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/dockets/build", async (
            BuildDocketRequest request,
            IDocketBuilder builder) =>
        {
            var result = await builder.BuildDocketAsync(
                request.RegisterId,
                request.ValidatorWalletAddress);

            return result.IsSuccess
                ? Results.Ok(result)
                : Results.BadRequest(result);
        })
        .WithName("BuildDocket")
        .WithSummary("Build new Docket from MemPool")
        .WithDescription("Collects pending Transactions, validates them, and builds a new Docket")
        .Produces<DocketBuildResult>(200)
        .Produces<DocketBuildResult>(400);

        // ... other endpoints

        return group;
    }
}

// In Program.cs
validationGroup.MapValidationEndpoints();
```

### 5.2 Phase 3 Deliverables

**Code:**
- ✅ External service clients (4)
- ✅ MemPoolManager with full implementation
- ✅ TransactionValidator (service layer)
- ✅ DocketBuilder with full implementation
- ✅ Validation endpoints (4)
- ✅ Test coverage > 80%

**Quality Gates:**
- ✅ All validation endpoints functional
- ✅ Can build Dockets from MemPool
- ✅ Can add Transactions to MemPool
- ✅ Integration tests passing

---

## 6. Phase 4: Consensus & Coordination

**Objective:** Implement ConsensusEngine, ValidatorOrchestrator, and GenesisManager.

**Duration:** 2 weeks

### 6.1 Tasks

#### Task 4.1: GenesisManager
**Estimated Effort:** 8 hours

**Deliverables:**
- `Services/IGenesisManager.cs`
- `Services/GenesisManager.cs`
- `Services/GenesisManagerTests.cs`

**Acceptance Criteria:**
- ✅ Creates genesis blocks for new Registers
- ✅ Validates GenesisConfig
- ✅ Computes genesis hash
- ✅ Stores to Register Service
- ✅ Test coverage > 80%

#### Task 4.2: ConsensusEngine
**Estimated Effort:** 16 hours

**Deliverables:**
- `Services/IConsensusEngine.cs`
- `Services/ConsensusEngine.cs`
- `Services/ConsensusEngineTests.cs`

**Acceptance Criteria:**
- ✅ Simple Quorum consensus algorithm
- ✅ Broadcasts Docket to validators (via Peer Service)
- ✅ Collects consensus votes with timeout
- ✅ Calculates quorum (percentage-based)
- ✅ Validates votes using `Sorcha.Validator.Core.ConsensusValidator`
- ✅ Attaches votes to Docket
- ✅ Test coverage > 80%

**Implementation:**
```csharp
public class ConsensusEngine(
    PeerServiceClient peerClient,
    WalletServiceClient walletClient,
    ILogger<ConsensusEngine> logger,
    IOptions<ValidatorServiceConfiguration> config) : IConsensusEngine
{
    public async Task<ConsensusResult> AchieveConsensusAsync(
        Docket docket,
        IEnumerable<string> validatorAddresses,
        CancellationToken ct = default)
    {
        var startTime = DateTimeOffset.UtcNow;
        var consensusConfig = config.Value.Consensus;

        // 1. Broadcast docket to validators
        await peerClient.BroadcastDocketAsync(docket, ct);

        // 2. Request votes from validators
        var votes = new List<ConsensusVote>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(consensusConfig.VoteTimeout);

        try
        {
            var voteResults = await peerClient.RequestConsensusVotesAsync(
                docket.RegisterId,
                docket,
                cts.Token);

            votes.AddRange(voteResults);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Consensus vote timeout for Docket {DocketNumber}", docket.DocketNumber);
        }

        // 3. Validate votes
        var validVotes = new List<ConsensusVote>();
        foreach (var vote in votes)
        {
            // Verify vote signature via Wallet Service
            var publicKey = await walletClient.GetPublicKeyAsync(vote.ValidatorAddress, ct);
            if (publicKey is null)
            {
                logger.LogWarning("Could not retrieve public key for validator {Address}", vote.ValidatorAddress);
                continue;
            }

            var voteValidation = Core.Validators.ConsensusValidator.ValidateConsensusVote(
                vote, docket.Hash, vote.ValidatorAddress, publicKey);

            if (voteValidation.IsValid)
            {
                validVotes.Add(vote);
            }
            else
            {
                logger.LogWarning("Invalid consensus vote from {Address}: {Errors}",
                    vote.ValidatorAddress, string.Join(", ", voteValidation.Errors.Select(e => e.Message)));
            }
        }

        // 4. Check quorum
        var approvedVotes = validVotes.Count(v => v.Approved);
        var totalVotes = validVotes.Count;

        if (totalVotes < consensusConfig.MinimumValidators)
        {
            var duration = DateTimeOffset.UtcNow - startTime;
            return ConsensusResult.Failed(
                $"Insufficient validators: {totalVotes} < {consensusConfig.MinimumValidators}",
                validVotes,
                duration);
        }

        var approvalRate = (double)approvedVotes / totalVotes;
        if (approvalRate < consensusConfig.QuorumPercentage)
        {
            var duration = DateTimeOffset.UtcNow - startTime;
            return ConsensusResult.Failed(
                $"Quorum not achieved: {approvalRate:P} < {consensusConfig.QuorumPercentage:P}",
                validVotes,
                duration);
        }

        // 5. Consensus achieved
        var successDuration = DateTimeOffset.UtcNow - startTime;
        logger.LogInformation(
            "Consensus achieved for Docket {DocketNumber}: {ApprovedVotes}/{TotalVotes} ({ApprovalRate:P})",
            docket.DocketNumber, approvedVotes, totalVotes, approvalRate);

        return ConsensusResult.Achieved(validVotes, successDuration);
    }
}
```

#### Task 4.3: ValidatorOrchestrator
**Estimated Effort:** 16 hours

**Deliverables:**
- `Services/IValidatorOrchestrator.cs`
- `Services/ValidatorOrchestrator.cs`
- `Services/ValidatorOrchestratorTests.cs`
- `BackgroundServices/ValidationBackgroundService.cs`

**Acceptance Criteria:**
- ✅ Coordinates DocketBuilder, ConsensusEngine, MemPoolManager
- ✅ Background service for continuous validation loop
- ✅ Per-Register state management (Running, Paused, Stopped)
- ✅ Start/Stop/Pause/Resume operations
- ✅ Error handling with exponential backoff
- ✅ Publishes events (Docket built, consensus achieved)
- ✅ Test coverage > 75%

**Implementation:**
```csharp
public class ValidatorOrchestrator(
    IDocketBuilder docketBuilder,
    IConsensusEngine consensusEngine,
    IMemPoolManager memPoolManager,
    RegisterServiceClient registerClient,
    ILogger<ValidatorOrchestrator> logger) : IValidatorOrchestrator
{
    private readonly ConcurrentDictionary<string, RegisterValidationState> _registerStates = new();

    public async Task StartValidationAsync(string registerId, CancellationToken ct = default)
    {
        var state = _registerStates.GetOrAdd(registerId, _ => new RegisterValidationState
        {
            RegisterId = registerId,
            State = ValidatorState.Starting
        });

        state.State = ValidatorState.Running;
        state.StartTime = DateTimeOffset.UtcNow;

        logger.LogInformation("Validation started for Register {RegisterId}", registerId);
    }

    // Background validation loop (in separate BackgroundService)
    private async Task ValidationLoopAsync(string registerId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var state = _registerStates[registerId];

                if (state.State != ValidatorState.Running)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    continue;
                }

                // 1. Build Docket
                var buildResult = await docketBuilder.BuildDocketAsync(
                    registerId,
                    state.ValidatorAddress,
                    ct);

                if (!buildResult.IsSuccess)
                {
                    await Task.Delay(config.Value.DocketBuildInterval, ct);
                    continue;
                }

                state.DocketsBuilt++;

                // 2. Achieve Consensus
                var consensusResult = await consensusEngine.AchieveConsensusAsync(
                    buildResult.Docket!,
                    state.ValidatorAddresses,
                    ct);

                if (!consensusResult.ConsensusAchieved)
                {
                    logger.LogWarning("Consensus failed for Docket {DocketNumber}: {Reason}",
                        buildResult.Docket.DocketNumber, consensusResult.FailureReason);
                    continue;
                }

                // 3. Store Docket
                buildResult.Docket.ConsensusVotes = consensusResult.Votes;
                buildResult.Docket.ConsensusState = ConsensusState.Approved;

                await registerClient.StoreDocketAsync(buildResult.Docket, ct);

                state.LastDocketNumber = buildResult.Docket.DocketNumber;
                state.TransactionsProcessed += buildResult.Docket.Transactions.Count;

                logger.LogInformation(
                    "Docket {DocketNumber} validated and stored for Register {RegisterId}",
                    buildResult.Docket.DocketNumber, registerId);

                // 4. Remove transactions from MemPool
                await memPoolManager.RemoveTransactionsAsync(
                    registerId,
                    buildResult.Docket.Transactions.Select(t => t.TxId),
                    ct);

                // 5. Wait for next interval
                await Task.Delay(config.Value.DocketBuildInterval, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in validation loop for Register {RegisterId}", registerId);
                await Task.Delay(TimeSpan.FromSeconds(30), ct); // Backoff
            }
        }
    }
}
```

#### Task 4.4: Admin & Metrics Endpoints
**Estimated Effort:** 8 hours

**Deliverables:**
- `Endpoints/AdminEndpoints.cs`
- `Endpoints/MetricsEndpoints.cs`

**Acceptance Criteria:**
- ✅ POST /api/admin/validation/start/{registerId}
- ✅ POST /api/admin/validation/stop/{registerId}
- ✅ POST /api/admin/validation/pause/{registerId}
- ✅ POST /api/admin/validation/resume/{registerId}
- ✅ GET /api/admin/validation/status
- ✅ GET /api/metrics/dockets
- ✅ GET /api/metrics/transactions
- ✅ GET /api/metrics/consensus
- ✅ GET /api/metrics/mempool
- ✅ All with OpenAPI documentation

#### Task 4.5: Genesis Endpoint
**Estimated Effort:** 4 hours

**Deliverables:**
- POST /api/validation/genesis endpoint

**Acceptance Criteria:**
- ✅ Creates genesis block
- ✅ Stores to Register Service
- ✅ Returns genesis Docket
- ✅ Integration test

### 6.2 Phase 4 Deliverables

**Code:**
- ✅ GenesisManager
- ✅ ConsensusEngine with Simple Quorum
- ✅ ValidatorOrchestrator with background service
- ✅ Admin endpoints (5)
- ✅ Metrics endpoints (4)
- ✅ Genesis endpoint
- ✅ Test coverage > 75%

**Quality Gates:**
- ✅ Can start/stop validation for Registers
- ✅ Background validation loop works
- ✅ Consensus coordination functional
- ✅ Genesis blocks can be created
- ✅ Metrics endpoints return data

---

## 7. Phase 5: Integration & Testing

**Objective:** Comprehensive integration testing and external service integration.

**Duration:** 2 weeks

### 7.1 Tasks

#### Task 5.1: Integration Test Suite
**Estimated Effort:** 20 hours

**Deliverables:**
- `tests/Sorcha.Validator.Service.Tests/Integration/DocketBuildingTests.cs`
- `tests/Sorcha.Validator.Service.Tests/Integration/ValidationEndpointsTests.cs`
- `tests/Sorcha.Validator.Service.Tests/Integration/ConsensusTests.cs`
- `tests/Sorcha.Validator.Service.Tests/Integration/AdminEndpointsTests.cs`
- `tests/Sorcha.Validator.Service.Tests/Integration/GenesisTests.cs`

**Acceptance Criteria:**
- ✅ End-to-end Docket building workflow
- ✅ End-to-end consensus workflow
- ✅ End-to-end genesis creation workflow
- ✅ All endpoints covered
- ✅ Mock external services (Wallet, Peer, Register, Blueprint)
- ✅ Test coverage > 70%

#### Task 5.2: External Service Integration
**Estimated Effort:** 12 hours

**Deliverables:**
- Integration with Wallet Service (signature verification)
- Integration with Peer Service (Docket broadcasting)
- Integration with Register Service (blockchain storage)
- Integration with Blueprint Service (schema retrieval)

**Acceptance Criteria:**
- ✅ Can call all external services successfully
- ✅ Handles external service failures gracefully
- ✅ Retry logic works
- ✅ Circuit breaker prevents cascading failures

#### Task 5.3: Performance Testing
**Estimated Effort:** 12 hours

**Deliverables:**
- `tests/Sorcha.Validator.Service.Tests/Performance/DocketBuildingBenchmarks.cs`
- `tests/Sorcha.Validator.Service.Tests/Performance/ValidationBenchmarks.cs`
- Load testing scripts (K6 or JMeter)

**Acceptance Criteria:**
- ✅ Docket building < 5s for 100 Transactions
- ✅ Docket validation < 2s for 100 Transactions
- ✅ Consensus < 30s for 3 validators
- ✅ MemPool throughput > 1000 tx/s

#### Task 5.4: Security Testing
**Estimated Effort:** 8 hours

**Deliverables:**
- Security test scenarios
- Penetration testing checklist
- Vulnerability scanning

**Acceptance Criteria:**
- ✅ No SQL injection vulnerabilities
- ✅ No XSS vulnerabilities
- ✅ Rate limiting prevents DoS
- ✅ Input validation prevents malicious data
- ✅ Signature verification works correctly

#### Task 5.5: Bug Fixes & Refinement
**Estimated Effort:** 16 hours

**Acceptance Criteria:**
- ✅ All critical bugs fixed
- ✅ All integration tests passing
- ✅ Performance targets met
- ✅ Security issues resolved

### 7.2 Phase 5 Deliverables

**Code:**
- ✅ Comprehensive integration test suite
- ✅ Performance benchmarks
- ✅ Security tests
- ✅ All bugs fixed

**Quality Gates:**
- ✅ All tests passing
- ✅ Test coverage targets met (90% Core, 70% Service)
- ✅ Performance targets met
- ✅ Security scan clean

---

## 8. Phase 6: Production Readiness

**Objective:** Documentation, deployment, monitoring, and final review.

**Duration:** 1 week

### 8.1 Tasks

#### Task 6.1: API Documentation
**Estimated Effort:** 8 hours

**Deliverables:**
- OpenAPI specification complete
- Scalar UI configured
- API usage examples
- Postman collection

**Acceptance Criteria:**
- ✅ All endpoints documented
- ✅ Request/response examples
- ✅ Error codes documented
- ✅ Authentication documented

#### Task 6.2: Operational Documentation
**Estimated Effort:** 8 hours

**Deliverables:**
- Deployment guide
- Configuration guide
- Monitoring guide
- Troubleshooting guide
- Runbook for common operations

**Acceptance Criteria:**
- ✅ Step-by-step deployment instructions
- ✅ Configuration examples
- ✅ Monitoring dashboards configured
- ✅ Common issues documented

#### Task 6.3: Enclave Support (Optional)
**Estimated Effort:** 16 hours

**Deliverables:**
- Intel SGX enclave build
- Remote attestation implementation
- Enclave deployment guide

**Acceptance Criteria:**
- ✅ Sorcha.Validator.Core compiles as SGX enclave
- ✅ Attestation works
- ✅ Validation runs in enclave

#### Task 6.4: Monitoring & Alerting
**Estimated Effort:** 8 hours

**Deliverables:**
- Prometheus metrics export
- Grafana dashboard
- Alert rules

**Acceptance Criteria:**
- ✅ All metrics exposed
- ✅ Dashboard shows key metrics
- ✅ Alerts configured for critical issues

#### Task 6.5: Final Review & Handoff
**Estimated Effort:** 8 hours

**Acceptance Criteria:**
- ✅ Code review complete
- ✅ All documentation reviewed
- ✅ Deployment tested
- ✅ Handoff to operations team

### 8.2 Phase 6 Deliverables

**Documentation:**
- ✅ API documentation complete
- ✅ Operational documentation complete
- ✅ Deployment guide
- ✅ Monitoring guide
- ✅ Runbook

**Production:**
- ✅ Service deployable to production
- ✅ Monitoring configured
- ✅ Alerts configured
- ✅ Enclave support (optional)

---

## 9. Risk Management

### 9.1 Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| **External service dependency delays** | Medium | High | Mock services for testing, parallel development |
| **Consensus algorithm complexity** | Medium | Medium | Start with Simple Quorum (Phase 1), defer BFT to Phase 2 |
| **Performance targets not met** | Low | High | Early performance testing, optimization sprints |
| **Enclave integration issues** | High | Medium | Make enclave optional (Phase 6), core library enclave-safe |
| **Security vulnerabilities** | Medium | Critical | Security testing in Phase 5, code review, penetration testing |
| **Test coverage gaps** | Medium | Medium | Continuous testing, coverage monitoring |

### 9.2 Dependencies

**Critical Dependencies:**
- Wallet Service (signature verification) - **Must have**
- Peer Service (Docket broadcasting) - **Must have**
- Register Service (blockchain storage) - **Must have**
- Blueprint Service (schema retrieval) - **Must have**

**Optional Dependencies:**
- Redis (caching) - **Nice to have**
- Enclave platform (SGX/SEV) - **Optional (Phase 6)**

---

## 10. Dependencies

### 10.1 External Services

| Service | Purpose | Status | Notes |
|---------|---------|--------|-------|
| Wallet Service | Signature verification | Required | Must be implemented first |
| Peer Service | Docket broadcasting | Required | Must be implemented first |
| Register Service | Blockchain storage | Required | Must be implemented first |
| Blueprint Service | Schema retrieval | Required | Already implemented |
| Redis | Distributed caching | Optional | Can use in-memory cache |

### 10.2 Infrastructure

| Component | Purpose | Status | Notes |
|-----------|---------|--------|-------|
| .NET 10 SDK | Development | Available | - |
| Docker | Containerization | Available | - |
| .NET Aspire | Orchestration | Available | - |
| Intel SGX SDK | Enclave support | Optional | Phase 6 only |

---

## 11. Timeline

### 11.1 Gantt Chart

```
Week 1-2: Phase 1 (Foundation)
  ├─ W1: Project setup, data models, DocketValidator
  └─ W2: TransactionValidator, ConsensusValidator, ChainValidator

Week 3: Phase 2 (Service Infrastructure)
  ├─ Service project setup
  ├─ Program.cs, middleware, configuration
  └─ AppHost integration

Week 4-5: Phase 3 (Validation Components)
  ├─ W4: External clients, MemPoolManager, TransactionValidator (service)
  └─ W5: DocketBuilder, Validation endpoints

Week 6-7: Phase 4 (Consensus & Coordination)
  ├─ W6: GenesisManager, ConsensusEngine
  └─ W7: ValidatorOrchestrator, Admin/Metrics endpoints

Week 8-9: Phase 5 (Integration & Testing)
  ├─ W8: Integration tests, external service integration
  └─ W9: Performance testing, security testing, bug fixes

Week 10: Phase 6 (Production Readiness)
  ├─ Documentation
  ├─ Monitoring/alerting
  ├─ Enclave support (optional)
  └─ Final review & handoff
```

### 11.2 Milestones

| Milestone | Date | Deliverables |
|-----------|------|--------------|
| **M1: Core Library Complete** | End of Week 2 | Sorcha.Validator.Core with 90%+ coverage |
| **M2: Service Infrastructure Ready** | End of Week 3 | Service starts, health checks working |
| **M3: Validation Functional** | End of Week 5 | Can build and validate Dockets |
| **M4: Consensus Functional** | End of Week 7 | Can achieve consensus, orchestration working |
| **M5: Testing Complete** | End of Week 9 | All tests passing, performance targets met |
| **M6: Production Ready** | End of Week 10 | Deployable to production |

---

## Appendix A: Task Checklist

### Phase 1: Foundation
- [ ] Task 1.1: Project Setup
- [ ] Task 1.2: Data Models
- [ ] Task 1.3: DocketValidator
- [ ] Task 1.4: TransactionValidator
- [ ] Task 1.5: ConsensusValidator
- [ ] Task 1.6: ChainValidator
- [ ] Task 1.7: HashingUtilities

### Phase 2: Service Infrastructure
- [ ] Task 2.1: Project Setup
- [ ] Task 2.2: Program.cs Setup
- [ ] Task 2.3: Configuration Models
- [ ] Task 2.4: Data Models
- [ ] Task 2.5: Middleware
- [ ] Task 2.6: AppHost Integration

### Phase 3: Validation Components
- [ ] Task 3.1: External Service Clients
- [ ] Task 3.2: MemPoolManager
- [ ] Task 3.3: TransactionValidator (Service)
- [ ] Task 3.4: DocketBuilder
- [ ] Task 3.5: Validation Endpoints

### Phase 4: Consensus & Coordination
- [ ] Task 4.1: GenesisManager
- [ ] Task 4.2: ConsensusEngine
- [ ] Task 4.3: ValidatorOrchestrator
- [ ] Task 4.4: Admin & Metrics Endpoints
- [ ] Task 4.5: Genesis Endpoint

### Phase 5: Integration & Testing
- [ ] Task 5.1: Integration Test Suite
- [ ] Task 5.2: External Service Integration
- [ ] Task 5.3: Performance Testing
- [ ] Task 5.4: Security Testing
- [ ] Task 5.5: Bug Fixes & Refinement

### Phase 6: Production Readiness
- [ ] Task 6.1: API Documentation
- [ ] Task 6.2: Operational Documentation
- [ ] Task 6.3: Enclave Support (Optional)
- [ ] Task 6.4: Monitoring & Alerting
- [ ] Task 6.5: Final Review & Handoff

---

**Document Version:** 1.0
**Last Updated:** 2025-11-16
**Status:** Implementation Plan - Ready for Execution
