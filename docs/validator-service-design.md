# Sorcha Validator Service - Design Specification

## Executive Summary

The Sorcha Validator Service is the blockchain consensus and validation component responsible for building, validating, and securing Dockets (blocks) in Sorcha Registers (distributed ledgers). It serves as the arbiter of shared stored truths in the Sorcha peer-to-peer network.

**Key Responsibilities:**
- Build and validate Dockets containing Transactions from Blueprint Actions
- Implement consensus mechanism for distributed validation across Peer networks
- Handle genesis block creation for new Registers
- Provide cryptographic validation using Wallet Service integration
- Support secure execution in enclaves/HSMs for production environments
- Expose administrative APIs for metrics, monitoring, and operational control

**Strategic Position:**
The Validator Service is the trust anchor of the Sorcha platform, ensuring data integrity, cryptographic verification, and distributed consensus for multi-participant workflows.

---

## 1. Background and Goals

### 1.1 Purpose

The Validator Service implements the blockchain consensus mechanism for Sorcha, providing:

1. **Docket Building** - Assembling validated Transactions into Dockets
2. **Docket Validation** - Verifying cryptographic signatures, chain integrity, and business rules
3. **Consensus Arbitration** - Coordinating validation across distributed Peer networks
4. **Genesis Management** - Creating the first block for new Registers
5. **Security Enforcement** - Protecting validation logic in secure enclaves

### 1.2 Goals

**Primary Goals:**
- ✅ Build tamper-proof Dockets with cryptographic chain integrity
- ✅ Validate all Transactions against Blueprint schemas and business rules
- ✅ Implement distributed consensus for multi-Peer validation
- ✅ Support genesis block creation for Register initialization
- ✅ Enable secure execution in enclaves (Intel SGX, AMD SEV, Azure Confidential Computing)
- ✅ Integrate with Wallet Service for signature verification and key management
- ✅ Provide comprehensive administrative APIs for operational control

**Non-Goals:**
- ❌ Transaction creation (handled by Blueprint Service)
- ❌ P2P network communication (handled by Peer Service)
- ❌ Blueprint execution (handled by Blueprint Engine)
- ❌ Wallet/key management (handled by Wallet Service)

### 1.3 Key Requirements (from SiccaV3 Analysis)

**Functional Requirements:**
1. **Docket Building** - Collect Transactions from MemPool, validate, and create Dockets
2. **Validation Engine** - Verify signatures, schemas, chain integrity, and business rules
3. **Consensus Mechanism** - Coordinate distributed validation (improved from SiccaV3)
4. **Genesis Handling** - Create first block with special validation rules
5. **MemPool Management** - Thread-safe per-Register Transaction pools with limits
6. **Chain Integrity** - SHA256-based blockchain with PreviousHash linkage

**Security Requirements:**
1. **Enclave Support** - Design for Intel SGX, AMD SEV, or HSM execution
2. **Cryptographic Isolation** - Wallet Service integration for key operations
3. **Input Validation** - Prevent injection, overflow, and malicious data
4. **Rate Limiting** - Prevent DoS attacks on validation endpoints
5. **Audit Logging** - Comprehensive security event logging

**Operational Requirements:**
1. **Admin APIs** - Start/stop validation, configure per-Register processing
2. **Metrics** - Docket throughput, validation latency, consensus success rate
3. **Health Checks** - Standard Sorcha health endpoints
4. **Configuration** - Runtime configuration for consensus parameters

---

## 2. Architecture

### 2.1 High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    Sorcha Validator Service                     │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  API Layer (Minimal APIs)                                        │
│  ┌──────────────┬──────────────┬──────────────┐                │
│  │ Validation   │ Admin        │ Metrics      │                │
│  │ Endpoints    │ Endpoints    │ Endpoints    │                │
│  └──────────────┴──────────────┴──────────────┘                │
│                         ↓                                        │
│  Service Layer                                                   │
│  ┌──────────────────────────────────────────────────────┐      │
│  │ ValidatorOrchestrator (Main Service)                 │      │
│  │ - Coordinates all validation operations              │      │
│  │ - Manages lifecycle and state                        │      │
│  └──────────────────────────────────────────────────────┘      │
│           ↓              ↓              ↓              ↓        │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌──────────┐│
│  │ Docket      │ │ Transaction │ │ Consensus   │ │ MemPool  ││
│  │ Builder     │ │ Validator   │ │ Engine      │ │ Manager  ││
│  └─────────────┘ └─────────────┘ └─────────────┘ └──────────┘│
│                                                                   │
│  Core Layer (Portable Libraries)                                 │
│  ┌──────────────────────────────────────────────────────┐      │
│  │ Sorcha.Validator.Core (Enclave-Safe)                 │      │
│  │ - Pure validation logic (no I/O, no network)         │      │
│  │ - Stateless, thread-safe, deterministic              │      │
│  │ - Can run in Intel SGX/AMD SEV enclaves              │      │
│  └──────────────────────────────────────────────────────┘      │
│                                                                   │
│  External Dependencies (via DI)                                  │
│  ┌──────────────┬──────────────┬──────────────┐                │
│  │ Wallet       │ Peer         │ Register     │                │
│  │ Service      │ Service      │ Service      │                │
│  │ (Signatures) │ (Broadcast)  │ (Storage)    │                │
│  └──────────────┴──────────────┴──────────────┘                │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘

External Integration:
  ↓ Wallet Service - Signature verification, key management
  ↓ Peer Service - Docket broadcasting, peer consensus coordination
  ↓ Register Service - Blockchain storage, chain queries
  ↓ Blueprint Service - Blueprint/schema retrieval for validation
```

### 2.2 Component Breakdown

#### 2.2.1 API Layer (ASP.NET Core Minimal APIs)

**Validation Endpoints** (`/api/validation`)
- `POST /api/validation/dockets/build` - Build new Docket from MemPool
- `POST /api/validation/dockets/validate` - Validate incoming Docket
- `POST /api/validation/transactions/add` - Add Transaction to MemPool
- `GET /api/validation/transactions/{registerId}` - Get pending Transactions
- `POST /api/validation/genesis` - Create genesis block for Register

**Admin Endpoints** (`/api/admin`)
- `POST /api/admin/validation/start/{registerId}` - Start validation for Register
- `POST /api/admin/validation/stop/{registerId}` - Stop validation for Register
- `POST /api/admin/validation/pause/{registerId}` - Pause validation temporarily
- `POST /api/admin/validation/resume/{registerId}` - Resume validation
- `GET /api/admin/validation/status` - Get validation status for all Registers
- `PUT /api/admin/config` - Update runtime configuration

**Metrics Endpoints** (`/api/metrics`)
- `GET /api/metrics/dockets` - Docket building/validation metrics
- `GET /api/metrics/transactions` - Transaction processing metrics
- `GET /api/metrics/consensus` - Consensus performance metrics
- `GET /api/metrics/mempool` - MemPool statistics

**Standard Endpoints** (via Service Defaults)
- `GET /health` - Health check (liveness + readiness)
- `GET /alive` - Liveness probe
- `GET /openapi/v1.json` - OpenAPI specification

#### 2.2.2 Service Layer

**IValidatorOrchestrator** - Main coordination service
```csharp
public interface IValidatorOrchestrator
{
    // Lifecycle management
    Task StartValidationAsync(string registerId, CancellationToken ct = default);
    Task StopValidationAsync(string registerId, CancellationToken ct = default);
    Task PauseValidationAsync(string registerId, CancellationToken ct = default);
    Task ResumeValidationAsync(string registerId, CancellationToken ct = default);

    // Status queries
    Task<ValidationStatus> GetStatusAsync(string registerId, CancellationToken ct = default);
    Task<IEnumerable<RegisterValidationState>> GetAllStatusesAsync(CancellationToken ct = default);
}
```

**IDocketBuilder** - Docket construction
```csharp
public interface IDocketBuilder
{
    // Build Docket from MemPool
    Task<DocketBuildResult> BuildDocketAsync(
        string registerId,
        string validatorWalletAddress,
        CancellationToken ct = default);

    // Create genesis block
    Task<Docket> CreateGenesisBlockAsync(
        string registerId,
        string creatorWalletAddress,
        GenesisConfig config,
        CancellationToken ct = default);
}
```

**ITransactionValidator** - Transaction validation
```csharp
public interface ITransactionValidator
{
    // Validate single transaction
    Task<TransactionValidationResult> ValidateAsync(
        Transaction transaction,
        ValidationContext context,
        CancellationToken ct = default);

    // Batch validation
    Task<IEnumerable<TransactionValidationResult>> ValidateBatchAsync(
        IEnumerable<Transaction> transactions,
        ValidationContext context,
        CancellationToken ct = default);
}
```

**IConsensusEngine** - Consensus coordination
```csharp
public interface IConsensusEngine
{
    // Coordinate consensus for Docket
    Task<ConsensusResult> AchieveConsensusAsync(
        Docket docket,
        IEnumerable<string> validatorAddresses,
        CancellationToken ct = default);

    // Validate consensus vote
    Task<bool> ValidateConsensusVoteAsync(
        ConsensusVote vote,
        Docket docket,
        CancellationToken ct = default);
}
```

**IMemPoolManager** - Transaction pool management
```csharp
public interface IMemPoolManager
{
    // Add transaction to pool
    Task<bool> AddTransactionAsync(
        string registerId,
        Transaction transaction,
        CancellationToken ct = default);

    // Get transactions for Docket building
    Task<IEnumerable<Transaction>> GetPendingTransactionsAsync(
        string registerId,
        int maxCount,
        CancellationToken ct = default);

    // Remove transactions (after Docket creation)
    Task RemoveTransactionsAsync(
        string registerId,
        IEnumerable<string> transactionIds,
        CancellationToken ct = default);

    // Get statistics
    Task<MemPoolStats> GetStatsAsync(string registerId, CancellationToken ct = default);
}
```

#### 2.2.3 Core Layer (Sorcha.Validator.Core)

**Enclave-Safe Validation Library**

This library contains **pure validation logic** that can run in secure enclaves:

```csharp
// NO I/O dependencies - all inputs passed as parameters
// NO network calls - purely computational
// NO mutable state - thread-safe and deterministic
// NO ASP.NET dependencies - portable to any .NET environment

public static class DocketValidator
{
    // Validate Docket structure and chain integrity
    public static ValidationResult ValidateDocket(
        Docket docket,
        Docket? previousDocket,
        ValidationRules rules)
    {
        // 1. Validate basic structure
        if (string.IsNullOrEmpty(docket.RegisterId))
            return ValidationResult.Failed("RegisterId is required");

        // 2. Validate chain integrity
        if (previousDocket != null)
        {
            if (docket.PreviousHash != previousDocket.Hash)
                return ValidationResult.Failed("PreviousHash mismatch");

            if (docket.DocketNumber != previousDocket.DocketNumber + 1)
                return ValidationResult.Failed("DocketNumber sequence invalid");
        }
        else if (!docket.IsGenesis)
        {
            return ValidationResult.Failed("Non-genesis block requires previous Docket");
        }

        // 3. Validate hash computation
        var computedHash = ComputeDocketHash(docket);
        if (docket.Hash != computedHash)
            return ValidationResult.Failed("Docket hash invalid");

        // 4. Validate timestamp
        if (previousDocket != null && docket.Timestamp <= previousDocket.Timestamp)
            return ValidationResult.Failed("Timestamp must be after previous Docket");

        return ValidationResult.Success();
    }

    // Compute SHA256 hash of Docket
    public static string ComputeDocketHash(Docket docket)
    {
        // Deterministic serialization for hashing
        var hashInput = $"{docket.RegisterId}|{docket.DocketNumber}|" +
                       $"{docket.PreviousHash}|{docket.Timestamp:O}|" +
                       $"{docket.ValidatorAddress}|" +
                       $"{string.Join("|", docket.Transactions.Select(t => t.TxId))}";

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(hashInput));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}

public static class TransactionValidator
{
    // Validate Transaction against Blueprint schema
    public static ValidationResult ValidateTransaction(
        Transaction transaction,
        Blueprint blueprint,
        Action action,
        string? previousTxHash)
    {
        // 1. Validate basic structure
        if (string.IsNullOrEmpty(transaction.TxId))
            return ValidationResult.Failed("TxId is required");

        // 2. Validate Blueprint/Action reference
        if (transaction.BlueprintId != blueprint.Id)
            return ValidationResult.Failed("BlueprintId mismatch");

        if (transaction.ActionId != action.Id)
            return ValidationResult.Failed("ActionId mismatch");

        // 3. Validate payload against JSON Schema
        if (action.DataSchemas != null && action.DataSchemas.Any())
        {
            var schemaValidation = ValidateAgainstSchemas(
                transaction.Payload,
                action.DataSchemas);

            if (!schemaValidation.IsValid)
                return schemaValidation;
        }

        // 4. Validate previous transaction reference (if chained)
        if (!string.IsNullOrEmpty(transaction.PreviousTxId) &&
            transaction.PreviousTxId != previousTxHash)
        {
            return ValidationResult.Failed("Previous transaction reference invalid");
        }

        return ValidationResult.Success();
    }

    private static ValidationResult ValidateAgainstSchemas(
        JsonNode payload,
        IEnumerable<JsonDocument> schemas)
    {
        // Use JSON Schema validation (JsonSchema.Net)
        foreach (var schemaDoc in schemas)
        {
            var schema = JsonSchema.FromText(schemaDoc.RootElement.GetRawText());
            var result = schema.Evaluate(payload, new EvaluationOptions
            {
                OutputFormat = OutputFormat.List
            });

            if (!result.IsValid)
            {
                var errors = result.Details
                    .Where(d => d.HasErrors)
                    .SelectMany(d => d.Errors ?? Enumerable.Empty<string>());

                return ValidationResult.Failed(errors.ToArray());
            }
        }

        return ValidationResult.Success();
    }
}

public static class ConsensusValidator
{
    // Validate consensus vote signature
    public static ValidationResult ValidateConsensusVote(
        ConsensusVote vote,
        string docketHash,
        string expectedValidatorAddress,
        byte[] publicKey)
    {
        // 1. Validate vote structure
        if (vote.DocketHash != docketHash)
            return ValidationResult.Failed("DocketHash mismatch");

        if (vote.ValidatorAddress != expectedValidatorAddress)
            return ValidationResult.Failed("ValidatorAddress mismatch");

        // 2. Verify signature (using provided public key)
        var voteData = $"{vote.DocketHash}|{vote.ValidatorAddress}|" +
                      $"{vote.Approved}|{vote.Timestamp:O}";

        var isValid = CryptographicOperations.VerifySignature(
            publicKey,
            Encoding.UTF8.GetBytes(voteData),
            Convert.FromHexString(vote.Signature));

        if (!isValid)
            return ValidationResult.Failed("Vote signature invalid");

        return ValidationResult.Success();
    }
}
```

**Key Characteristics of Core Layer:**
- ✅ **No I/O** - All data passed as parameters
- ✅ **No Network** - Pure computation only
- ✅ **Stateless** - No mutable state
- ✅ **Deterministic** - Same input = same output
- ✅ **Thread-Safe** - Can run in parallel
- ✅ **Enclave-Compatible** - Works in SGX/SEV/HSM
- ✅ **Highly Testable** - Pure functions, easy to unit test

---

## 3. Data Models

### 3.1 Core Models

**Docket** (Block)
```csharp
public class Docket
{
    [Required]
    public string RegisterId { get; set; } = string.Empty;

    [Required]
    public int DocketNumber { get; set; }

    [Required]
    public string Hash { get; set; } = string.Empty;

    [Required]
    public string PreviousHash { get; set; } = "0000000000000000000000000000000000000000000000000000000000000000";

    [Required]
    public DateTimeOffset Timestamp { get; set; }

    [Required]
    public string ValidatorAddress { get; set; } = string.Empty;

    [Required]
    public List<Transaction> Transactions { get; set; } = [];

    public bool IsGenesis { get; set; } = false;

    public string? GenesisConfig { get; set; } // JSON metadata for genesis

    // Consensus metadata
    public List<ConsensusVote> ConsensusVotes { get; set; } = [];
    public ConsensusState ConsensusState { get; set; } = ConsensusState.Pending;

    // Computed properties
    public int TransactionCount => Transactions.Count;
    public long Size => EstimateSize();

    private long EstimateSize()
    {
        // Estimate in bytes (for size limits)
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(this).Length;
    }
}
```

**Transaction**
```csharp
public class Transaction
{
    [Required]
    public string TxId { get; set; } = string.Empty;

    [Required]
    public string RegisterId { get; set; } = string.Empty;

    [Required]
    public string BlueprintId { get; set; } = string.Empty;

    [Required]
    public int ActionId { get; set; }

    [Required]
    public string SenderAddress { get; set; } = string.Empty;

    public string? TargetAddress { get; set; }

    [Required]
    public JsonNode Payload { get; set; } = JsonNode.Parse("{}")!;

    [Required]
    public DateTimeOffset Timestamp { get; set; }

    [Required]
    public string Signature { get; set; } = string.Empty;

    public string? PreviousTxId { get; set; } // For transaction chains

    // Metadata
    public Dictionary<string, string> Metadata { get; set; } = [];
}
```

**ConsensusVote**
```csharp
public class ConsensusVote
{
    [Required]
    public string DocketHash { get; set; } = string.Empty;

    [Required]
    public string ValidatorAddress { get; set; } = string.Empty;

    [Required]
    public bool Approved { get; set; }

    [Required]
    public DateTimeOffset Timestamp { get; set; }

    [Required]
    public string Signature { get; set; } = string.Empty;

    public string? Reason { get; set; } // Rejection reason if not approved
}
```

**GenesisConfig**
```csharp
public class GenesisConfig
{
    [Required]
    public string RegisterId { get; set; } = string.Empty;

    [Required]
    public string RegisterName { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required]
    public string CreatorAddress { get; set; } = string.Empty;

    public List<string> InitialValidators { get; set; } = [];

    public Dictionary<string, JsonNode> InitialState { get; set; } = [];

    public ConsensusConfiguration? ConsensusConfig { get; set; }
}
```

### 3.2 Configuration Models

**ValidatorServiceConfiguration**
```csharp
public class ValidatorServiceConfiguration
{
    // General settings
    public bool Enabled { get; set; } = true;
    public string WalletServiceUrl { get; set; } = "http://wallet-service";
    public string PeerServiceUrl { get; set; } = "http://peer-service";
    public string RegisterServiceUrl { get; set; } = "http://register-service";
    public string BlueprintServiceUrl { get; set; } = "http://blueprint-service";

    // Docket building settings
    public int MaxTransactionsPerDocket { get; set; } = 100;
    public int MaxDocketSizeBytes { get; set; } = 1_048_576; // 1 MB
    public TimeSpan DocketBuildInterval { get; set; } = TimeSpan.FromSeconds(10);

    // MemPool settings
    public int MaxMemPoolSizePerRegister { get; set; } = 10_000;
    public TimeSpan TransactionExpirationTime { get; set; } = TimeSpan.FromHours(24);

    // Consensus settings
    public ConsensusConfiguration Consensus { get; set; } = new();

    // Security settings
    public SecurityConfiguration Security { get; set; } = new();
}

public class ConsensusConfiguration
{
    public ConsensusType Type { get; set; } = ConsensusType.SimpleQuorum;
    public double QuorumPercentage { get; set; } = 0.67; // 67% for 2-of-3
    public int MinimumValidators { get; set; } = 1;
    public TimeSpan VoteTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

public class SecurityConfiguration
{
    public bool RequireEnclaveAttestation { get; set; } = false;
    public string? EnclaveType { get; set; } // "SGX", "SEV", "HSM", null
    public bool RequireSignatureVerification { get; set; } = true;
    public int MaxValidationConcurrency { get; set; } = 10;
    public bool EnableRateLimiting { get; set; } = true;
    public int RateLimitPerMinute { get; set; } = 1000;
}
```

### 3.3 Result Models

**DocketBuildResult**
```csharp
public record DocketBuildResult
{
    public bool IsSuccess { get; init; }
    public Docket? Docket { get; init; }
    public List<string> Errors { get; init; } = [];
    public DocketBuildMetrics Metrics { get; init; } = new();

    public static DocketBuildResult Success(Docket docket, DocketBuildMetrics metrics) => new()
    {
        IsSuccess = true,
        Docket = docket,
        Metrics = metrics
    };

    public static DocketBuildResult Failed(params string[] errors) => new()
    {
        IsSuccess = false,
        Errors = errors.ToList()
    };
}

public record DocketBuildMetrics
{
    public int TransactionsProcessed { get; init; }
    public int TransactionsIncluded { get; init; }
    public int TransactionsRejected { get; init; }
    public TimeSpan BuildDuration { get; init; }
    public long DocketSizeBytes { get; init; }
}
```

**TransactionValidationResult**
```csharp
public record TransactionValidationResult
{
    public bool IsValid { get; init; }
    public string TransactionId { get; init; } = string.Empty;
    public List<ValidationError> Errors { get; init; } = [];

    public static TransactionValidationResult Valid(string txId) => new()
    {
        IsValid = true,
        TransactionId = txId
    };

    public static TransactionValidationResult Invalid(string txId, params ValidationError[] errors) => new()
    {
        IsValid = false,
        TransactionId = txId,
        Errors = errors.ToList()
    };
}

public record ValidationError
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Field { get; init; }
    public ValidationSeverity Severity { get; init; } = ValidationSeverity.Error;
}

public enum ValidationSeverity
{
    Warning,
    Error,
    Critical
}
```

**ConsensusResult**
```csharp
public record ConsensusResult
{
    public bool ConsensusAchieved { get; init; }
    public int TotalValidators { get; init; }
    public int ApprovedVotes { get; init; }
    public int RejectedVotes { get; init; }
    public List<ConsensusVote> Votes { get; init; } = [];
    public TimeSpan Duration { get; init; }
    public string? FailureReason { get; init; }

    public static ConsensusResult Achieved(List<ConsensusVote> votes, TimeSpan duration) => new()
    {
        ConsensusAchieved = true,
        TotalValidators = votes.Count,
        ApprovedVotes = votes.Count(v => v.Approved),
        RejectedVotes = votes.Count(v => !v.Approved),
        Votes = votes,
        Duration = duration
    };

    public static ConsensusResult Failed(string reason, List<ConsensusVote> votes, TimeSpan duration) => new()
    {
        ConsensusAchieved = false,
        TotalValidators = votes.Count,
        ApprovedVotes = votes.Count(v => v.Approved),
        RejectedVotes = votes.Count(v => !v.Approved),
        Votes = votes,
        Duration = duration,
        FailureReason = reason
    };
}
```

---

## 4. API Specifications

### 4.1 Validation Endpoints

#### POST /api/validation/dockets/build
Build a new Docket from pending Transactions in MemPool.

**Request:**
```json
{
  "registerId": "reg_abc123",
  "validatorWalletAddress": "0x1234...abcd",
  "maxTransactions": 100
}
```

**Response (200 OK):**
```json
{
  "isSuccess": true,
  "docket": {
    "registerId": "reg_abc123",
    "docketNumber": 42,
    "hash": "a3b5c7...",
    "previousHash": "d8e9f1...",
    "timestamp": "2025-11-16T10:30:00Z",
    "validatorAddress": "0x1234...abcd",
    "transactions": [...],
    "isGenesis": false
  },
  "metrics": {
    "transactionsProcessed": 150,
    "transactionsIncluded": 100,
    "transactionsRejected": 50,
    "buildDuration": "00:00:02.5",
    "docketSizeBytes": 524288
  }
}
```

#### POST /api/validation/dockets/validate
Validate an incoming Docket from another Peer.

**Request:**
```json
{
  "docket": {
    "registerId": "reg_abc123",
    "docketNumber": 42,
    "hash": "a3b5c7...",
    "previousHash": "d8e9f1...",
    "timestamp": "2025-11-16T10:30:00Z",
    "validatorAddress": "0x5678...efgh",
    "transactions": [...]
  }
}
```

**Response (200 OK):**
```json
{
  "isValid": true,
  "errors": [],
  "validationDuration": "00:00:01.2"
}
```

**Response (400 Bad Request):**
```json
{
  "isValid": false,
  "errors": [
    {
      "code": "HASH_MISMATCH",
      "message": "Docket hash does not match computed hash",
      "severity": "Critical"
    },
    {
      "code": "SIGNATURE_INVALID",
      "message": "Transaction signature verification failed",
      "field": "transactions[3].signature",
      "severity": "Critical"
    }
  ],
  "validationDuration": "00:00:00.8"
}
```

#### POST /api/validation/transactions/add
Add a Transaction to the MemPool for future Docket inclusion.

**Request:**
```json
{
  "transaction": {
    "txId": "tx_xyz789",
    "registerId": "reg_abc123",
    "blueprintId": "bp_456",
    "actionId": 2,
    "senderAddress": "0x1234...abcd",
    "payload": { "field1": "value1" },
    "timestamp": "2025-11-16T10:25:00Z",
    "signature": "abc123..."
  }
}
```

**Response (200 OK):**
```json
{
  "accepted": true,
  "memPoolSize": 42,
  "estimatedInclusionTime": "00:00:08"
}
```

#### POST /api/validation/genesis
Create genesis block for a new Register.

**Request:**
```json
{
  "registerId": "reg_new123",
  "registerName": "Supply Chain Register",
  "description": "Tracks supply chain Blueprint executions",
  "creatorAddress": "0x1234...abcd",
  "initialValidators": [
    "0x1234...abcd",
    "0x5678...efgh",
    "0x9abc...ijkl"
  ],
  "initialState": {
    "chainId": "sorcha-main",
    "version": "1.0"
  },
  "consensusConfig": {
    "type": "SimpleQuorum",
    "quorumPercentage": 0.67,
    "minimumValidators": 2
  }
}
```

**Response (201 Created):**
```json
{
  "docket": {
    "registerId": "reg_new123",
    "docketNumber": 0,
    "hash": "genesis_a3b5...",
    "previousHash": "0000000000000000000000000000000000000000000000000000000000000000",
    "timestamp": "2025-11-16T10:00:00Z",
    "validatorAddress": "0x1234...abcd",
    "transactions": [],
    "isGenesis": true,
    "genesisConfig": "{...}"
  }
}
```

### 4.2 Admin Endpoints

#### POST /api/admin/validation/start/{registerId}
Start validation processing for a Register.

**Response (200 OK):**
```json
{
  "registerId": "reg_abc123",
  "status": "Running",
  "message": "Validation started successfully"
}
```

#### GET /api/admin/validation/status
Get validation status for all Registers.

**Response (200 OK):**
```json
{
  "registers": [
    {
      "registerId": "reg_abc123",
      "status": "Running",
      "lastDocketNumber": 42,
      "memPoolSize": 15,
      "uptime": "02:30:45",
      "docketsBuilt": 42,
      "transactionsProcessed": 1250
    },
    {
      "registerId": "reg_xyz789",
      "status": "Paused",
      "lastDocketNumber": 18,
      "memPoolSize": 5,
      "uptime": "01:15:30",
      "docketsBuilt": 18,
      "transactionsProcessed": 540
    }
  ]
}
```

### 4.3 Metrics Endpoints

#### GET /api/metrics/dockets
Get Docket building and validation metrics.

**Response (200 OK):**
```json
{
  "totalDocketsBuilt": 125,
  "totalDocketsValidated": 450,
  "averageBuildTime": "00:00:02.3",
  "averageValidationTime": "00:00:01.1",
  "averageTransactionsPerDocket": 87.5,
  "consensusSuccessRate": 0.98,
  "last24Hours": {
    "docketsBuilt": 42,
    "docketsValidated": 156,
    "averageBuildTime": "00:00:02.1"
  }
}
```

---

## 5. Security and Enclave Support

### 5.1 Enclave Execution Model

**Architecture for Secure Enclaves:**

```
┌─────────────────────────────────────────────────────────────┐
│                   Untrusted Environment                     │
│                                                               │
│  ┌───────────────────────────────────────────────────────┐ │
│  │ Validator Service (ASP.NET Core)                      │ │
│  │                                                         │ │
│  │  - API Endpoints (public)                              │ │
│  │  - I/O Operations (database, network)                  │ │
│  │  - Orchestration                                       │ │
│  └───────────────────────────────────────────────────────┘ │
│                          ↕                                  │
│                 Enclave Boundary                            │
│                   (Intel SGX / AMD SEV / Azure CC)         │
│                          ↕                                  │
│  ┌───────────────────────────────────────────────────────┐ │
│  │ Trusted Enclave                                        │ │
│  │                                                         │ │
│  │  Sorcha.Validator.Core (Pure .NET)                    │ │
│  │  ┌─────────────────────────────────────────────────┐ │ │
│  │  │ • DocketValidator.ValidateDocket()              │ │ │
│  │  │ • TransactionValidator.ValidateTransaction()    │ │ │
│  │  │ • ConsensusValidator.ValidateConsensusVote()    │ │ │
│  │  │ • CryptoOperations.VerifySignature()            │ │ │
│  │  └─────────────────────────────────────────────────┘ │ │
│  │                                                         │ │
│  │  Characteristics:                                      │ │
│  │  ✓ No I/O (all data passed in/out)                   │ │
│  │  ✓ No network access                                  │ │
│  │  ✓ Stateless (no mutable state)                      │ │
│  │  ✓ Cryptographically attested                        │ │
│  │  ✓ Memory encrypted                                   │ │
│  └───────────────────────────────────────────────────────┘ │
│                          ↕                                  │
│  ┌───────────────────────────────────────────────────────┐ │
│  │ Wallet Service Enclave (Optional)                     │ │
│  │                                                         │ │
│  │  - Private key storage                                 │ │
│  │  - Signature operations                                │ │
│  └───────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### 5.2 Enclave Integration Patterns

**Development Environment** (No Enclave)
```csharp
// In Program.cs (development)
builder.Services.AddSingleton<IValidationCore, ValidationCore>();

public class ValidationCore : IValidationCore
{
    public ValidationResult ValidateDocket(Docket docket, Docket? previous)
    {
        // Direct call to Sorcha.Validator.Core
        return DocketValidator.ValidateDocket(docket, previous, _rules);
    }
}
```

**Production Environment** (Intel SGX Enclave)
```csharp
// In Program.cs (production with SGX)
builder.Services.AddSingleton<IValidationCore, SgxValidationCore>();

public class SgxValidationCore : IValidationCore
{
    private readonly SgxEnclave _enclave;

    public SgxValidationCore()
    {
        // Load enclave with Sorcha.Validator.Core compiled as enclave binary
        _enclave = SgxEnclave.Create("Sorcha.Validator.Core.signed.so");

        // Perform remote attestation
        var attestationReport = _enclave.GetAttestationReport();
        VerifyAttestation(attestationReport);
    }

    public ValidationResult ValidateDocket(Docket docket, Docket? previous)
    {
        // Serialize inputs
        var input = SerializeDocketValidationInput(docket, previous);

        // Call enclave (encrypted memory, no I/O inside)
        var output = _enclave.CallFunction("ValidateDocket", input);

        // Deserialize result
        return DeserializeValidationResult(output);
    }
}
```

**Production Environment** (Azure Confidential Computing)
```csharp
// In Program.cs (Azure CC)
builder.Services.AddSingleton<IValidationCore, AzureConfidentialValidationCore>();

public class AzureConfidentialValidationCore : IValidationCore
{
    private readonly ConfidentialVirtualMachine _cvm;

    public ValidationResult ValidateDocket(Docket docket, Docket? previous)
    {
        // Call validation in confidential VM with SEV-SNP
        var request = new ValidationRequest
        {
            Docket = docket,
            PreviousDocket = previous
        };

        var response = await _cvm.InvokeAsync<ValidationResponse>(
            "ValidateDocket",
            request);

        return response.Result;
    }
}
```

### 5.3 Security Best Practices

**1. Input Validation**
```csharp
// Always validate inputs before passing to enclave
public async Task<ValidationResult> ValidateDocketAsync(Docket docket)
{
    // Pre-validation (prevent malicious inputs)
    if (docket.Transactions.Count > 10_000)
        return ValidationResult.Failed("Too many transactions");

    if (docket.Size > 10_000_000) // 10 MB limit
        return ValidationResult.Failed("Docket too large");

    // Sanitize inputs
    docket.RegisterId = SanitizeId(docket.RegisterId);

    // Pass to enclave for cryptographic validation
    return await _validationCore.ValidateDocketAsync(docket);
}
```

**2. Rate Limiting**
```csharp
// In Program.cs
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: partition => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 1000,
                Window = TimeSpan.FromMinutes(1)
            });
    });
});

app.UseRateLimiter();
```

**3. Signature Verification**
```csharp
// Always verify signatures via Wallet Service
public async Task<bool> VerifyTransactionSignatureAsync(Transaction tx)
{
    var request = new SignatureVerificationRequest
    {
        Data = SerializeTransactionForSigning(tx),
        Signature = tx.Signature,
        PublicKeyOrAddress = tx.SenderAddress
    };

    var response = await _walletServiceClient.PostAsync<VerificationResponse>(
        "/api/signatures/verify",
        request);

    return response.IsValid;
}
```

**4. Audit Logging**
```csharp
// Log all critical operations
_logger.LogInformation(
    "Docket validation completed: RegisterId={RegisterId}, DocketNumber={DocketNumber}, " +
    "IsValid={IsValid}, Duration={Duration}ms, ValidatorAddress={ValidatorAddress}",
    docket.RegisterId,
    docket.DocketNumber,
    result.IsValid,
    duration.TotalMilliseconds,
    docket.ValidatorAddress);

// Log security events
_logger.LogWarning(
    SecurityEventIds.SignatureVerificationFailed,
    "Signature verification failed: TxId={TxId}, SenderAddress={SenderAddress}",
    tx.TxId,
    tx.SenderAddress);
```

---

## 6. Integration with Other Services

### 6.1 Wallet Service Integration

**Purpose:** Signature verification and key management

**Usage:**
```csharp
public class WalletServiceClient(HttpClient httpClient)
{
    // Verify transaction signature
    public async Task<bool> VerifySignatureAsync(
        string data,
        string signature,
        string address,
        CancellationToken ct = default)
    {
        var request = new
        {
            data,
            signature,
            address
        };

        var response = await httpClient.PostAsJsonAsync(
            "/api/signatures/verify",
            request,
            ct);

        if (!response.IsSuccessStatusCode)
            return false;

        var result = await response.Content.ReadFromJsonAsync<VerificationResult>(ct);
        return result?.IsValid ?? false;
    }

    // Sign consensus vote (when this node is validator)
    public async Task<string> SignConsensusVoteAsync(
        string docketHash,
        string validatorAddress,
        bool approved,
        CancellationToken ct = default)
    {
        var voteData = $"{docketHash}|{validatorAddress}|{approved}|{DateTimeOffset.UtcNow:O}";

        var request = new
        {
            data = voteData,
            address = validatorAddress
        };

        var response = await httpClient.PostAsJsonAsync(
            "/api/signatures/sign",
            request,
            ct);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SignatureResult>(ct);
        return result?.Signature ?? throw new InvalidOperationException("Signature failed");
    }
}
```

### 6.2 Peer Service Integration

**Purpose:** Docket broadcasting and consensus coordination

**Usage:**
```csharp
public class PeerServiceClient(HttpClient httpClient)
{
    // Broadcast Docket to all peers in Register
    public async Task BroadcastDocketAsync(
        Docket docket,
        CancellationToken ct = default)
    {
        await httpClient.PostAsJsonAsync(
            $"/api/peers/broadcast/dockets",
            docket,
            ct);
    }

    // Request consensus votes from validators
    public async Task<IEnumerable<ConsensusVote>> RequestConsensusVotesAsync(
        string registerId,
        Docket docket,
        CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"/api/peers/consensus/request",
            new { registerId, docket },
            ct);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<ConsensusVote>>(ct)
               ?? [];
    }
}
```

### 6.3 Register Service Integration

**Purpose:** Blockchain storage and chain queries

**Usage:**
```csharp
public class RegisterServiceClient(HttpClient httpClient)
{
    // Store validated Docket
    public async Task StoreDocketAsync(
        Docket docket,
        CancellationToken ct = default)
    {
        await httpClient.PostAsJsonAsync(
            $"/api/registers/{docket.RegisterId}/dockets",
            docket,
            ct);
    }

    // Get previous Docket for chain validation
    public async Task<Docket?> GetPreviousDocketAsync(
        string registerId,
        int docketNumber,
        CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync(
            $"/api/registers/{registerId}/dockets/{docketNumber - 1}",
            ct);

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<Docket>(ct);
    }

    // Get latest Docket number
    public async Task<int> GetLatestDocketNumberAsync(
        string registerId,
        CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync(
            $"/api/registers/{registerId}/dockets/latest",
            ct);

        if (!response.IsSuccessStatusCode)
            return 0;

        var docket = await response.Content.ReadFromJsonAsync<Docket>(ct);
        return docket?.DocketNumber ?? 0;
    }
}
```

### 6.4 Blueprint Service Integration

**Purpose:** Blueprint/schema retrieval for validation

**Usage:**
```csharp
public class BlueprintServiceClient(HttpClient httpClient)
{
    // Get Blueprint for Transaction validation
    public async Task<Blueprint?> GetBlueprintAsync(
        string blueprintId,
        CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync(
            $"/api/blueprints/{blueprintId}",
            ct);

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<Blueprint>(ct);
    }

    // Get specific Action for validation
    public async Task<Action?> GetActionAsync(
        string blueprintId,
        int actionId,
        CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync(
            $"/api/blueprints/{blueprintId}/actions/{actionId}",
            ct);

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<Action>(ct);
    }
}
```

---

## 7. Deployment Considerations

### 7.1 Service Configuration (appsettings.json)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Sorcha.Validator": "Debug"
    }
  },
  "AllowedHosts": "*",

  "ValidatorService": {
    "Enabled": true,
    "WalletServiceUrl": "http://wallet-service",
    "PeerServiceUrl": "http://peer-service",
    "RegisterServiceUrl": "http://register-service",
    "BlueprintServiceUrl": "http://blueprint-service",

    "MaxTransactionsPerDocket": 100,
    "MaxDocketSizeBytes": 1048576,
    "DocketBuildInterval": "00:00:10",

    "MaxMemPoolSizePerRegister": 10000,
    "TransactionExpirationTime": "1.00:00:00",

    "Consensus": {
      "Type": "SimpleQuorum",
      "QuorumPercentage": 0.67,
      "MinimumValidators": 1,
      "VoteTimeout": "00:00:30"
    },

    "Security": {
      "RequireEnclaveAttestation": false,
      "EnclaveType": null,
      "RequireSignatureVerification": true,
      "MaxValidationConcurrency": 10,
      "EnableRateLimiting": true,
      "RateLimitPerMinute": 1000
    }
  }
}
```

### 7.2 Docker Deployment (Development)

```dockerfile
# Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Services/Sorcha.Validator.Service/Sorcha.Validator.Service.csproj", "Services/Sorcha.Validator.Service/"]
COPY ["src/Common/Sorcha.Validator.Core/Sorcha.Validator.Core.csproj", "Common/Sorcha.Validator.Core/"]
COPY ["src/Common/Sorcha.ServiceDefaults/Sorcha.ServiceDefaults.csproj", "Common/Sorcha.ServiceDefaults/"]
RUN dotnet restore "Services/Sorcha.Validator.Service/Sorcha.Validator.Service.csproj"

COPY src/ .
WORKDIR "/src/Services/Sorcha.Validator.Service"
RUN dotnet build "Sorcha.Validator.Service.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Sorcha.Validator.Service.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Sorcha.Validator.Service.dll"]
```

### 7.3 .NET Aspire Orchestration

```csharp
// In AppHost.cs
var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure
var redis = builder.AddRedis("redis").WithRedisCommander();

// Services
var walletService = builder.AddProject<Projects.Sorcha_WalletService_Api>("wallet-service")
    .WithReference(redis);

var blueprintService = builder.AddProject<Projects.Sorcha_Blueprint_Service>("blueprint-service")
    .WithReference(redis);

var peerService = builder.AddProject<Projects.Sorcha_Peer_Service>("peer-service")
    .WithReference(redis);

var registerService = builder.AddProject<Projects.Sorcha_Register_Service>("register-service")
    .WithReference(redis);

// Validator Service with all dependencies
var validatorService = builder.AddProject<Projects.Sorcha_Validator_Service>("validator-service")
    .WithReference(redis)
    .WithReference(walletService)
    .WithReference(blueprintService)
    .WithReference(peerService)
    .WithReference(registerService);

// API Gateway
var apiGateway = builder.AddProject<Projects.Sorcha_ApiGateway>("api-gateway")
    .WithReference(validatorService)
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

### 7.4 Production Deployment (Azure Confidential Computing)

```bicep
# Azure Bicep template for confidential VMs
resource validatorVM 'Microsoft.Compute/virtualMachines@2023-03-01' = {
  name: 'sorcha-validator-cvm'
  location: resourceGroup().location
  properties: {
    hardwareProfile: {
      vmSize: 'Standard_DC4as_v5' // AMD SEV-SNP confidential VM
    }
    securityProfile: {
      securityType: 'ConfidentialVM'
      uefiSettings: {
        secureBootEnabled: true
        vTpmEnabled: true
      }
      encryptionAtHost: true
    }
    storageProfile: {
      imageReference: {
        publisher: 'Canonical'
        offer: '0001-com-ubuntu-confidential-vm-jammy'
        sku: '22_04-lts-cvm'
        version: 'latest'
      }
      osDisk: {
        createOption: 'FromImage'
        managedDisk: {
          securityProfile: {
            securityEncryptionType: 'DiskWithVMGuestState'
          }
        }
      }
    }
  }
}
```

---

## 8. Testing Strategy

### 8.1 Unit Tests (Sorcha.Validator.Core.Tests)

**Focus:** Pure validation logic

```csharp
public class DocketValidatorTests
{
    [Fact]
    public void ValidateDocket_WithValidGenesis_ReturnsSuccess()
    {
        // Arrange
        var genesis = new Docket
        {
            RegisterId = "reg_test",
            DocketNumber = 0,
            Hash = "genesis_hash",
            PreviousHash = "0000000000000000000000000000000000000000000000000000000000000000",
            Timestamp = DateTimeOffset.UtcNow,
            ValidatorAddress = "0x1234",
            Transactions = [],
            IsGenesis = true
        };

        // Act
        var result = DocketValidator.ValidateDocket(genesis, null, new ValidationRules());

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateDocket_WithInvalidHash_ReturnsFailure()
    {
        // Arrange
        var docket = new Docket
        {
            RegisterId = "reg_test",
            DocketNumber = 1,
            Hash = "invalid_hash",
            PreviousHash = "genesis_hash",
            Timestamp = DateTimeOffset.UtcNow,
            ValidatorAddress = "0x1234",
            Transactions = []
        };

        // Act
        var result = DocketValidator.ValidateDocket(docket, null, new ValidationRules());

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "HASH_INVALID");
    }

    [Fact]
    public void ComputeDocketHash_WithSameInput_ReturnsSameHash()
    {
        // Arrange
        var docket = new Docket
        {
            RegisterId = "reg_test",
            DocketNumber = 1,
            PreviousHash = "genesis",
            Timestamp = new DateTimeOffset(2025, 11, 16, 10, 0, 0, TimeSpan.Zero),
            ValidatorAddress = "0x1234",
            Transactions = []
        };

        // Act
        var hash1 = DocketValidator.ComputeDocketHash(docket);
        var hash2 = DocketValidator.ComputeDocketHash(docket);

        // Assert
        hash1.Should().Be(hash2);
    }
}
```

### 8.2 Integration Tests (Sorcha.Validator.Service.Tests)

**Focus:** Service coordination and external dependencies

```csharp
public class ValidatorOrchestratorTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    [Fact]
    public async Task BuildDocket_WithPendingTransactions_CreatesValidDocket()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Add transactions to MemPool
        for (int i = 0; i < 10; i++)
        {
            await client.PostAsJsonAsync("/api/validation/transactions/add", new
            {
                transaction = CreateTestTransaction(i)
            });
        }

        // Act
        var response = await client.PostAsJsonAsync("/api/validation/dockets/build", new
        {
            registerId = "reg_test",
            validatorWalletAddress = "0x1234",
            maxTransactions = 10
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<DocketBuildResult>();
        result.IsSuccess.Should().BeTrue();
        result.Docket.Transactions.Should().HaveCount(10);
    }
}
```

### 8.3 Test Coverage Targets

- **Sorcha.Validator.Core:** 90%+ (critical validation logic)
- **Sorcha.Validator.Service:** 70%+ (service orchestration)
- **Overall:** 80%+

---

## 9. Success Criteria

### 9.1 Functional Requirements

✅ Build Dockets from MemPool with configurable size limits
✅ Validate Dockets with cryptographic chain integrity
✅ Create genesis blocks for new Registers
✅ Implement distributed consensus mechanism
✅ Integrate with Wallet Service for signature verification
✅ Support enclave/HSM execution for production security
✅ Provide admin APIs for operational control
✅ Expose metrics for monitoring and observability

### 9.2 Performance Requirements

- **Docket Building:** < 5 seconds for 100 transactions
- **Docket Validation:** < 2 seconds for 100 transactions
- **Consensus Coordination:** < 30 seconds for 3-validator quorum
- **MemPool Throughput:** > 1000 transactions/second
- **API Latency (P95):** < 500ms

### 9.3 Security Requirements

✅ All Transaction signatures verified via Wallet Service
✅ Rate limiting on all public endpoints
✅ Audit logging for all critical operations
✅ Input validation prevents injection attacks
✅ Enclave support for production deployments
✅ No private keys in Validator Service (delegated to Wallet Service)

---

## 10. Future Enhancements

**Phase 2:**
- Byzantine Fault Tolerance (BFT) consensus
- Proof-of-Stake validator selection
- Cross-Register transaction validation
- Smart contract validation (if Sorcha adds smart contracts)

**Phase 3:**
- Zero-Knowledge Proof (ZKP) validation
- Quantum-resistant signature algorithms
- Sharding for horizontal scalability
- Layer 2 rollup support

---

## Appendix A: Consensus Mechanisms

### Simple Quorum (Phase 1)

**Algorithm:**
1. Validator builds Docket
2. Broadcasts Docket to peer validators
3. Each validator validates and votes (approve/reject)
4. If quorum reached (e.g., 2-of-3 = 67%), Docket is accepted
5. Store to Register

**Pros:**
- Simple to implement
- Low latency
- Works for trusted validator sets

**Cons:**
- Not Byzantine fault tolerant
- Requires known validator set

### Practical Byzantine Fault Tolerance (Future)

**Algorithm:**
1. Pre-prepare: Leader proposes Docket
2. Prepare: Validators vote on proposal
3. Commit: Validators commit if 2f+1 votes received (f = max faults)
4. Execute: Store to Register after commit

**Pros:**
- Tolerates Byzantine faults (malicious validators)
- Proven algorithm (used in Hyperledger, etc.)

**Cons:**
- Higher latency (3 rounds)
- More complex implementation

---

## Appendix B: Related Documentation

- `alpha-validator-service-analysis.md` - Alpha Validator analysis
- `SORCHA-VALIDATOR-DESIGN-RECOMMENDATIONS.md` - Design recommendations
- `VALIDATOR-SERVICE-QUICK-REFERENCE.md` - Developer quick reference
- `/docs/architecture.md` - Overall Sorcha architecture
- `/docs/blueprint-schema.md` - Blueprint data format
- `/.specify/UNIFIED-DESIGN-SUMMARY.md` - Unified design summary

---

**Document Version:** 1.0
**Last Updated:** 2025-11-16
**Author:** Claude Code (Anthropic)
**Status:** Design Specification - Ready for Review
