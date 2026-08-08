# Sorcha Register Service

**Version**: 1.0.0
**Status**: Production Ready (100% Complete)
**Framework**: .NET 10.0
**Architecture**: Microservice

---

## Overview

The **Register Service** is the foundational distributed ledger component of the Sorcha platform, providing immutable transaction storage with blockchain-style chain integrity. It manages the complete lifecycle of distributed ledger registers (ledgers), transactions, and dockets (blocks), ensuring data immutability, auditability, and cryptographic integrity.

This service acts as the central data store for:
- **Distributed ledger management** (create, update, query registers)
- **Transaction storage** with cryptographic chain linking
- **Docket (block) management** with SHA256 hash verification
- **Advanced querying** via OData V4 and LINQ
- **Real-time notifications** for ledger state changes
- **Subscription-scoped access control** for enterprise deployments

### Key Features

- **Register Management**: Full CRUD operations for distributed ledger instances with subscription-scoped access control
- **Transaction Storage**: Immutable transaction persistence with blockchain-style chain integrity (prevTxId links)
- **Docket Management**: Seal transactions into blocks (dockets) with SHA256 hashing and chain validation
- **OData V4 Queries**: Advanced query capabilities with $filter, $select, $orderby, $top, $skip, $count
- **Address Indexing**: Efficient queries by sender/recipient wallet addresses
- **Blueprint Tracking**: Query transactions by blueprint ID and instance ID for workflow correlation
- **Real-time Notifications**: SignalR hub (`/hubs/register`) for live ledger state updates
- **Event-Driven Architecture**: Publish/subscribe patterns for loose coupling with Validator and Wallet services
- **DID Support**: JSON-LD transaction format with Decentralized Identifier (DID) URIs for semantic web integration
- **Storage Flexibility**: Pluggable storage abstraction (MongoDB, PostgreSQL, In-Memory)
- **Chain Validation**: Cryptographic verification of transaction and docket chain integrity
- **Statistics & Analytics**: Transaction statistics, register counts, and performance metrics

---

## Architecture

### Components

```
Register Service
├── API Layer (Minimal APIs)
│   ├── Registers API (CRUD, stats)
│   ├── Transactions API (submit, query)
│   ├── Dockets API (retrieve, validate)
│   └── Query API (OData, advanced queries)
├── SignalR Hubs
│   └── RegisterHub (/hubs/register)
├── Business Logic Layer
│   ├── RegisterManager (register lifecycle)
│   ├── TransactionManager (transaction storage)
│   ├── QueryManager (advanced queries)
│   └── SubscriptionResolver (subscription-scoped access)
├── Storage Abstraction
│   ├── IRegisterRepository (interface)
│   ├── InMemoryRegisterRepository (testing)
│   ├── MongoRegisterRepository (production)
│   └── PostgreSQLRegisterRepository (production)
├── Event System
│   ├── IEventPublisher (event abstraction)
│   ├── IEventSubscriber (subscription abstraction)
│   ├── InMemoryEventPublisher (testing)
│   └── AspireEventPublisher (production)
└── External Integrations
    ├── Validator Service (chain validation, consensus)
    └── Wallet Service (address verification)
```

### Data Flow

```
Client → Register API → [Create Register]
      ↓
Blueprint Service → Validator Service → [Submit Transaction to Mempool]
      ↓
Validator Mempool → DocketBuildTrigger → [Build Docket from Pending Transactions]
      ↓
Consensus → [Achieve Consensus (single-validator auto-approve)]
      ↓
DocketDistributor → Register Docket API → [Write Docket + Transactions, Update Height]
      ↓
Blueprint Service → Register Transaction API → [Poll for Confirmation]
      ↓
SignalR Hub → [Notify Clients: DocketSealed, RegisterHeightUpdated]
```

> **Note:** Action transactions are NOT submitted directly to the Register Service.
> They flow through the Validator Service pipeline (mempool → docket sealing → Register write-back).
> The direct `POST /api/registers/{id}/transactions` endpoint is restricted to internal/diagnostic use only.

### Blockchain-Style Chain Integrity

The Register Service implements blockchain-style chain integrity through transaction and docket linking:

**Transaction Chain:**
```
Genesis Transaction (prevTxId: "")
    ↓
Transaction 1 (prevTxId: genesis_hash)
    ↓
Transaction 2 (prevTxId: tx1_hash)
    ↓
Transaction 3 (prevTxId: tx2_hash)
```

**Docket Chain (Blocks):**
```
Genesis Docket (previousHash: "")
    ↓
Docket 1 (previousHash: genesis_hash, contains: [tx1, tx2, tx3])
    ↓
Docket 2 (previousHash: docket1_hash, contains: [tx4, tx5, tx6])
```

### DID Support (Decentralized Identifiers)

All transactions are addressable via W3C-compliant DID URIs:
- **DID Format**: `did:sorcha:register:{registerId}/tx/{txId}`
- **JSON-LD Context**: `https://sorcha.dev/contexts/blockchain/v1.jsonld`
- **Example**: `did:sorcha:register:abc123def456/tx:7f3a8b2c...`

This enables:
- Universal transaction addressability
- Semantic web integration
- Interoperability with W3C standards
- Decentralized identity verification

---

## Quick Start

### Prerequisites

- **.NET 10 SDK** or later
- **Git**
- *Optional*: **Docker Desktop** (for Redis caching)

### 1. Clone and Navigate

```bash
git clone https://github.com/Sorcha-Platform/Sorcha.git
cd Sorcha/src/Services/Sorcha.Register.Service
```

### 2. Set Up Configuration

The service uses `appsettings.json` for configuration. For local development, defaults are pre-configured with in-memory storage.

### 3. Run the Service

```bash
dotnet run
```

Service will start at:
- **HTTPS**: `https://localhost:7085`
- **HTTP**: `http://localhost:5085`
- **Scalar API Docs**: `https://localhost:7085/scalar`
- **SignalR Hub**: `https://localhost:7085/hubs/register`
- **OData Endpoint**: `https://localhost:7085/odata/Transactions`

---

## Configuration

### appsettings.json Structure

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "MongoDB": "mongodb://localhost:27017/sorcha_register",
    "PostgreSQL": "Host=localhost;Database=sorcha_register;Username=postgres;Password=yourpassword",
    "Redis": "localhost:6379"
  },
  "ServiceUrls": {
    "ValidatorService": "https://localhost:7086",
    "WalletService": "https://localhost:7084"
  },
  "OpenTelemetry": {
    "ServiceName": "Sorcha.Register.Service",
    "ZipkinEndpoint": "http://localhost:9411"
  },
  "Register": {
    "StorageProvider": "InMemory",
    "EventProvider": "InMemory",
    "MaxTransactionsPerPage": 100,
    "DocketSealingInterval": "00:10:00"
  }
}
```

### Environment Variables

For production deployment:

```bash
# Storage connections
CONNECTIONSTRINGS__MONGODB="mongodb+srv://username:password@cluster.mongodb.net/sorcha_register"
CONNECTIONSTRINGS__POSTGRESQL="Host=prod-db;Database=sorcha_register;Username=svc_register;Password=your-secret"
CONNECTIONSTRINGS__REDIS="redis-prod.cache.windows.net:6380,password=your-redis-key,ssl=True"

# External service URLs
SERVICEURLS__VALIDATORSERVICE="https://validator.sorcha.io"
SERVICEURLS__WALLETSERVICE="https://wallet.sorcha.io"

# Observability
OPENTELEMETRY__ZIPKINENDPOINT="https://zipkin.yourcompany.com"

# Register configuration
REGISTER__STORAGEPROVIDER="MongoDB"
REGISTER__EVENTPROVIDER="AspireMessaging"
```

---

## API Endpoints

### Register Management

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/registers/` | Get all registers (subscription-scoped + system registers) |
| GET | `/api/registers/{id}` | Get register by ID |
| POST | `/api/registers/` | Create new register (requires `CanManageRegisters`) |
| PUT | `/api/registers/{id}` | Update register metadata |
| DELETE | `/api/registers/{id}` | Delete register (attestation-based auth; system registers cannot be deleted) |
| GET | `/api/registers/stats/count` | Get total register count |

### Transaction Management

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/registers/{registerId}/transactions` | Submit transaction (internal/diagnostic only, requires `CanWriteDockets`) |
| GET | `/api/registers/{registerId}/transactions/{txId}` | Get transaction by ID |
| GET | `/api/registers/{registerId}/transactions` | Get all transactions (paginated) |

> **Pipeline change:** Action transactions should be submitted to the Validator Service (`POST /api/v1/transactions/validate`), not directly to this endpoint. The Validator queues transactions in the mempool, builds dockets, and writes confirmed transactions back to the Register via the docket endpoint.

### Query API (Advanced)

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/query/wallets/{address}/transactions` | Get all transactions for wallet address |
| GET | `/api/query/senders/{address}/transactions` | Get transactions sent by address |
| GET | `/api/query/blueprints/{blueprintId}/transactions` | Get transactions for blueprint |
| GET | `/api/query/stats` | Get transaction statistics for register |

### Docket Management

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/registers/{registerId}/dockets` | Get all dockets (blocks) for register |
| GET | `/api/registers/{registerId}/dockets/{docketId}` | Get docket by ID (block height) |
| GET | `/api/registers/{registerId}/dockets/{docketId}/transactions` | Get transactions in docket |

### Public Federation — anonymous read (Feature 175)

Cross-installation federation: a **public** (advertised) register is open data, readable by any node —
including one from another installation holding **no credential here** — so it can bootstrap trust by
replicating and cryptographically verifying the register on its own side. There is **no separate
public URL namespace**: the canonical register/docket **read** endpoints are **credential-gated** —
the credential (or its absence + the register's `Advertise` flag) governs the read. An **anonymous**
caller may read a register/docket only when the register is **public** (advertised); a private
register requires authentication. The gate is per-request, so a public→private flip takes effect
immediately. The read endpoints are **rate-limited** (burst-tolerant `Relaxed` policy — a full-register
replication pull is not throttled). **All writes** keep their existing auth (no cross-installation write).

| Method | Endpoint | Anonymous access |
|--------|----------|------------------|
| GET | `/api/registers/{id}` | When public (advertised); else 403 to anonymous, 404 if unknown |
| GET | `/api/registers/{registerId}/dockets` | When public; else 403 to anonymous |
| GET | `/api/registers/{registerId}/dockets/{docketId}` | When public; else 403 to anonymous |

> Peer federation authenticates by **node identity** (self-signed P-256 node cert, mTLS), not an
> installation-scoped service token — see the Peer Service. The peer docket-sync serving path reads
> **through to this Register Service on a cache miss** (including an empty owner-cache entry), so a
> public register's dockets are served for replication even when the owning node's peer cache is cold.
> Replicated dockets are verified fail-closed before persist (`DocketFinalizationService`); a
> create-on-sync genesis whose control-record `RegisterId` disagrees with its slot is rejected. A future
> ADR moves full docket verification into the Register service (see `specs/175-federation-public-read/research.md`).

### OData Query Endpoint

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/odata/Transactions` | Query transactions with OData V4 syntax |
| GET | `/odata/Registers` | Query registers with OData V4 syntax |
| GET | `/odata/Dockets` | Query dockets with OData V4 syntax |

**OData Example Queries:**

```bash
# Get first 10 transactions ordered by timestamp
GET /odata/Transactions?$top=10&$orderby=TimeStamp desc

# Filter transactions by sender address
GET /odata/Transactions?$filter=SenderWallet eq '1A2B3C4D5E6F...'

# Select specific fields
GET /odata/Transactions?$select=TxId,SenderWallet,TimeStamp

# Count transactions
GET /odata/Transactions?$count=true

# Complex filter
GET /odata/Transactions?$filter=contains(SenderWallet,'1A2B') and TimeStamp gt 2025-01-01
```

### Register Policy (Feature 048)

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/registers/{registerId}/policy` | Get current register policy (defaults if none set) |
| POST | `/api/registers/{registerId}/policy/update` | Propose policy update (governance-controlled) |
| GET | `/api/registers/{registerId}/policy/history` | Get policy version history (paginated) |
| GET | `/api/registers/{registerId}/validators/approved` | List on-chain approved validators |
| GET | `/api/registers/{registerId}/validators/operational` | List operationally active validators (Redis TTL) |

> **Register Creation** now accepts an optional `policy` field and an optional `purpose` field (`General` or `System`) in the creation request. If `policy` is omitted, default policy values are applied at genesis. If `purpose` is omitted, it defaults to `General`. Creating a `System` register requires the `CanCreateSystemRegisters` policy.

### System Register (Feature 048, upgraded in Feature 057)

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/system-register` | Get System Register metadata and status |
| GET | `/api/system-register/blueprints` | List system blueprints (paginated) |
| GET | `/api/system-register/blueprints/{blueprintId}` | Get specific system blueprint |
| GET | `/api/system-register/blueprints/{blueprintId}/versions/{version}` | Get specific blueprint version |
| POST | `/api/system-register/publish` | Publish a new blueprint to the system register |

> The **System Register** is a real register backed by the standard ledger infrastructure. It is bootstrapped on first startup using a pre-signed genesis block. Blueprint entries are stored as control-chain transactions on the well-known system register (ID: `aebf26362e079087571ac0932d4db973`).

#### Bootstrap Mode Configuration (Feature 100)

The `SystemRegister:BootstrapMode` setting controls how the system register is obtained on startup:

| Mode | Behaviour | Use Case |
|------|-----------|----------|
| `Auto` (default) | Brief peer sync (14s), then ingest embedded genesis | Local development (`docker-compose up`) |
| `SyncOnly` | Wait for peer sync indefinitely (5s fast retries for 2 min, then 5 min polling) | Production nodes joining an existing network |
| `GenesisFile` | Ingest genesis file immediately, no peer sync | First node creating a new network |

```json
{
  "SystemRegister": {
    "BootstrapMode": "SyncOnly",
    "GenesisFile": null,
    "FastRetryIntervalSeconds": 5,
    "FastRetryDurationSeconds": 120,
    "BackoffIntervalSeconds": 300
  }
}
```

Environment variable override: `SystemRegister__BootstrapMode=SyncOnly`

### RegisterPurpose

Registers are classified by a `RegisterPurpose` enum:

| Value | Description |
|-------|-------------|
| `General` | Default purpose. Standard registers created by organisations for workflow data. |
| `System` | Platform-internal registers used for system operations (e.g., the well-known system register). Creating a system register requires the `CanCreateSystemRegisters` policy (SystemAdmin only). System registers cannot be deleted. |

### Transaction Receipts (Feature 079)

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/registers/{registerId}/receipts/batch` | Store receipt batch (internal, `CanWriteDockets`) |
| GET | `/api/registers/{registerId}/transactions/{txId}/receipt` | Get receipt by transaction ID |
| GET | `/api/registers/{registerId}/dockets/{docketNumber}/receipts` | List docket receipts (paginated) |
| POST | `/api/registers/{registerId}/receipts/verify` | Verify receipt (public, stateless) |

### Merkle Inclusion Proofs (Feature 079)

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/registers/{registerId}/transactions/{txId}/inclusion-proof` | Generate inclusion proof on-demand |
| POST | `/api/registers/{registerId}/inclusion-proofs/verify` | Verify proof (public, stateless) |

### Transaction Revocation (Feature 079)

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/registers/{registerId}/transactions/revoke` | Submit revocation transaction |
| GET | `/api/registers/{registerId}/transactions/{txId}/status` | Get transaction lifecycle status (active/revoked/superseded) |

### Verification Bundles (Feature 079)

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/registers/{registerId}/transactions/{txId}/verification-bundle` | Export portable verification bundle |
| POST | `/api/registers/{registerId}/verification-bundles/verify` | Verify bundle (public, stateless) |

### SignalR Hub

| Hub | Endpoint | Events |
|-----|----------|--------|
| RegisterHub | `/hubs/register` | `RegisterCreated`, `RegisterDeleted`, `TransactionConfirmed`, `DocketSealed`, `RegisterHeightUpdated`, `TransactionReceipt` |

**SignalR Methods:**
- `SubscribeToRegister(registerId)` - Subscribe to register-specific events
- `UnsubscribeFromRegister(registerId)` - Unsubscribe from register

Notifications use **register-scoped groups** (`register:{registerId}`). Clients join a group per register they are interested in. The previous tenant-scoped groups (`SubscribeToTenant`/`UnsubscribeFromTenant`) have been removed.

For full API documentation with request/response schemas, open **Scalar UI** at `https://localhost:7085/scalar`.

---

## Development

### Project Structure

```
Sorcha.Register.Service/
├── Program.cs                      # Service entry point, minimal API definitions
├── Hubs/
│   └── RegisterHub.cs              # SignalR real-time notifications
└── appsettings.json                # Configuration

Core Libraries:
├── Sorcha.Register.Core/           # Business logic
│   ├── Managers/
│   │   ├── RegisterManager.cs      # Register lifecycle management
│   │   ├── TransactionManager.cs   # Transaction storage and validation
│   │   └── QueryManager.cs         # Advanced query operations
│   ├── Storage/
│   │   └── IRegisterRepository.cs  # Repository abstraction
│   ├── Events/
│   │   ├── IEventPublisher.cs      # Event publishing abstraction
│   │   └── RegisterEvents.cs       # Event definitions
│   └── Validators/
│       └── ChainValidator.cs       # Chain integrity validation
├── Sorcha.Register.Storage.InMemory/  # In-memory storage (testing)
├── Sorcha.Register.Models/         # Domain models
│   ├── Register.cs                 # Register entity
│   ├── TransactionModel.cs         # Transaction with JSON-LD support
│   ├── Docket.cs                   # Docket (block) entity
│   ├── PayloadModel.cs             # Encrypted payload
│   └── Enums/                      # Status enumerations
```

### Running Tests

```bash
# Run all Register Service tests
dotnet test tests/Sorcha.Register.Core.Tests
dotnet test tests/Sorcha.Register.Service.Tests

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Watch mode (auto-rerun on changes)
dotnet watch test --project tests/Sorcha.Register.Core.Tests
```

### Code Coverage

**Current Coverage**: ~92%
**Tests**: 112 unit + integration tests
- **Core Tests**: 10 test classes (RegisterManager, TransactionManager, QueryManager, Models, Validators)
- **Service Tests**: 3 integration test classes (RegisterAPI, TransactionAPI, QueryAPI, SignalR)
**Lines of Code**: ~4,150 LOC

```bash
# Generate coverage report
dotnet test --collect:"XPlat Code Coverage"
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage -reporttypes:Html
```

Open `coverage/index.html` in your browser.

---

## Integration with Other Services

### Validator Service Integration

The Register Service integrates with the Validator Service for:
- **Docket Write-Back**: Validator builds dockets from mempool transactions and writes sealed dockets (with transactions) back to the Register via `POST /api/registers/{id}/dockets`
- **Register Height Tracking**: Height is updated on each docket write (count-based: height = number of dockets written)
- **Idempotent Writes**: A duplicate docket/transaction insert (same docket number or transaction id) is treated as a successful idempotent retry **only when the persisted record matches** — the docket hash (or transaction signature) is compared before returning success. A same-number/different-hash docket (or same-id/different-signature transaction) is a genuine integrity divergence: the write is rejected with `409 Conflict` and a `LogCritical` entry rather than silently dropping the incoming docket's transactions (issue #814)
- **Register Monitoring**: Validator queries register height and latest docket to determine next docket number and chain from previous hash

**Communication**: HTTP REST API (Validator → Register for writes, Register → Validator for genesis submission)
**Key Endpoints**: `POST /dockets` (write-back), `GET /dockets/latest` (chain info), `GET /registers/{id}` (height)

### Wallet Service Integration

The Register Service integrates with the Wallet Service for:
- **Address Verification**: Validate wallet addresses exist
- **Transaction Queries**: Index transactions by sender/recipient addresses
- **Wallet History**: Provide transaction history for wallet displays

**Communication**: HTTP REST API
**Endpoints Used**: `/api/v1/wallets/{address}` (for verification)

### Blueprint Service Integration

The Register Service integrates with the Blueprint Service for:
- **Transaction Confirmation**: Blueprint Service polls `GET /api/registers/{id}/transactions/{txId}` to confirm action transactions have been sealed in a docket
- **Blueprint Tracking**: Store blueprint metadata in transactions (via Validator docket write-back)
- **Workflow Queries**: Query transactions by blueprint ID and instance ID

**Communication**: HTTP REST API
**Endpoints Used**: `GET /transactions/{txId}` (confirmation polling), `GET /transactions` (queries)
**Flow**: Blueprint Service submits action transactions to the **Validator Service** (not directly to Register), then polls Register for confirmation after docket sealing

### SignalR Client Example

**TypeScript/JavaScript:**

```typescript
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
    .withUrl("https://localhost:7085/hubs/register")
    .withAutomaticReconnect()
    .build();

connection.on("TransactionConfirmed", (registerId: string, transactionId: string) => {
    console.log(`Transaction ${transactionId} confirmed in register ${registerId}`);
});

connection.on("DocketSealed", (registerId: string, docketId: number, hash: string) => {
    console.log(`Docket ${docketId} sealed in register ${registerId} with hash ${hash}`);
});

await connection.start();
await connection.invoke("SubscribeToRegister", "your-register-id");
```

**C#/.NET:**

```csharp
using Microsoft.AspNetCore.SignalR.Client;

var connection = new HubConnectionBuilder()
    .WithUrl("https://localhost:7085/hubs/register")
    .WithAutomaticReconnect()
    .Build();

connection.On<string, string>("TransactionConfirmed", (registerId, transactionId) =>
{
    Console.WriteLine($"Transaction {transactionId} confirmed in register {registerId}");
});

await connection.StartAsync();
await connection.InvokeAsync("SubscribeToRegister", "your-register-id");
```

---

## Data Models

### Register

Represents a distributed ledger instance.

```csharp
public class Register
{
    public string Id { get; set; }              // GUID without hyphens (32 chars)
    public string Name { get; set; }            // Human-readable name (1-38 chars)
    public uint Height { get; set; }            // Current block height
    public RegisterStatus Status { get; set; }  // Offline, Online, Checking, Recovery
    public RegisterPurpose Purpose { get; set; } // General (default) or System
    public bool Advertise { get; set; }         // Network visibility
    public bool IsFullReplica { get; set; }     // Full history or partial
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

> **Note:** The `TenantId` property has been removed from the Register entity. Organisational access is now controlled through subscription records managed by the Tenant Service. Users see only registers their organisation is subscribed to, plus any system registers.

### TransactionModel

Represents a signed blockchain transaction with JSON-LD support.

```csharp
public class TransactionModel
{
    [JsonPropertyName("@context")]
    public string? Context { get; set; }        // JSON-LD context URI

    [JsonPropertyName("@type")]
    public string? Type { get; set; }           // "Transaction"

    [JsonPropertyName("@id")]
    public string? Id { get; set; }             // DID URI

    public string RegisterId { get; set; }      // Parent register
    public string TxId { get; set; }            // 64 char hex hash
    public string PrevTxId { get; set; }        // Previous transaction (chain link)
    public ulong? DocketNumber { get; set; }     // Docket number
    public uint Version { get; set; }           // Transaction format version
    public string SenderWallet { get; set; }    // Sender address
    public IEnumerable<string> RecipientsWallets { get; set; }  // Recipient addresses
    public DateTime TimeStamp { get; set; }
    public TransactionMetaData? MetaData { get; set; }          // Blueprint metadata
    public PayloadModel[] Payloads { get; set; }                // Encrypted data
    public string Signature { get; set; }       // Cryptographic signature

    public string GenerateDidUri() => $"did:sorcha:register:{RegisterId}/tx/{TxId}";
}
```

### Docket

Represents a sealed block of transactions.

```csharp
public class Docket
{
    public ulong Id { get; set; }               // Block height
    public string RegisterId { get; set; }      // Parent register
    public string PreviousHash { get; set; }    // Previous docket hash (chain link)
    public string Hash { get; set; }            // SHA256 hash of this docket
    public List<string> TransactionIds { get; set; }  // Transactions in block
    public DateTime TimeStamp { get; set; }
    public DocketState State { get; set; }      // Init, Proposed, Accepted, Sealed
}
```

### PayloadModel

Encrypted data within a transaction.

```csharp
public class PayloadModel
{
    public string[] WalletAccess { get; set; }  // Authorized wallet addresses
    public ulong PayloadSize { get; set; }      // Size in bytes
    public string Hash { get; set; }            // SHA-256 integrity hash
    public string Data { get; set; }            // Encrypted data (Base64)
    public Challenge? IV { get; set; }          // Initialization vector
    public Challenge[]? Challenges { get; set; } // Per-wallet challenges
}
```

---

## Security Considerations

### Data Protection

- **Encrypted Payloads**: All transaction payloads encrypted using AES-256-GCM
- **Wallet-Based Access**: Selective disclosure enforced through wallet access lists
- **Chain Integrity**: SHA256 hashing prevents tampering with transaction and docket chains
- **Signature Verification**: All transactions must be cryptographically signed

### Authentication

- JWT bearer token authentication required for all endpoints (issued by Tenant Service)
- Anonymous access to register creation has been removed

### Authorization

- **Register Creation**: Requires `CanManageRegisters` policy (admin role + `org_id` claim)
- **System Register Creation**: Requires `CanCreateSystemRegisters` policy (SystemAdmin only)
- **Register Queries**: Subscription-scoped — users see only registers their organisation is subscribed to, plus system registers (visible to all authenticated users)
- **Register Deletion**: Uses attestation-based authorization — the requesting user's `wallet_address` claim is matched against the register's control record. System registers cannot be deleted.
- **Transaction Verification**: Validate sender wallet ownership via signatures

### Secrets Management

- **Connection Strings**: Store in Azure Key Vault or environment variables
- **TLS Encryption**: Use TLS 1.3 for all communications
- **No Sensitive Logging**: Never log transaction payloads or encryption keys

---

## Deployment

### .NET Aspire (Development)

The Register Service is registered in the Aspire AppHost:

```csharp
var registerService = builder.AddProject<Projects.Sorcha_Register_Service>("register-service")
    .WithReference(redis);
```

Start the entire platform:

```bash
dotnet run --project src/Apps/Sorcha.AppHost
```

Access Aspire Dashboard: `http://localhost:15888`

### Docker

```bash
# Build Docker image
docker build -t sorcha-register-service:latest -f src/Services/Sorcha.Register.Service/Dockerfile .

# Run container
docker run -d \
  -p 7085:8080 \
  -e ConnectionStrings__MongoDB="mongodb://mongo:27017/sorcha_register" \
  -e ServiceUrls__ValidatorService="http://validator-service:8080" \
  -e ServiceUrls__WalletService="http://wallet-service:8080" \
  --name register-service \
  sorcha-register-service:latest
```

### Azure Deployment

Deploy to Azure Container Apps with:
- **Azure Cosmos DB (MongoDB API)**: Production document storage
- **Azure Cache for Redis**: Distributed caching and query cache
- **Azure Key Vault**: Connection strings and secrets
- **Application Insights**: Observability and monitoring

---

## Observability

### Logging (Serilog + Seq)

Structured logging with Serilog:

```csharp
Log.Information("Transaction {TxId} stored in register {RegisterId}", txId, registerId);
Log.Warning("Chain validation failed for register {RegisterId}: {Reason}", registerId, reason);
```

**Log Sinks**:
- Console (structured output via Serilog)
- OTLP → Aspire Dashboard (centralized log aggregation)

### Tracing (OpenTelemetry + Zipkin)

Distributed tracing with OpenTelemetry:

```bash
# View traces in Zipkin
open http://localhost:9411
```

**Traced Operations**:
- HTTP requests
- Transaction storage operations
- Docket sealing operations
- Query executions
- SignalR connections

### Metrics (Prometheus)

Metrics exposed at `/metrics`:
- Request count and latency
- Transaction submission rate
- Docket sealing rate
- Query performance metrics
- SignalR connection count
- Storage operation latency

---

## Troubleshooting

### Common Issues

**Issue**: SignalR hub connection fails
**Solution**: Ensure CORS is configured for client origin. Check `AllowedHosts` in appsettings.json.

```bash
# Test SignalR connectivity
curl -I https://localhost:7085/hubs/register
```

**Issue**: Transaction chain validation fails
**Solution**: Verify prevTxId links are correct. Use ChainValidator to diagnose broken chains.

```csharp
var validator = new ChainValidator(repository);
var results = await validator.ValidateRegisterChainAsync(registerId);
// Inspect results for broken links
```

**Issue**: OData queries not working
**Solution**: Ensure OData middleware is configured in Program.cs. Check query syntax.

```bash
# Test OData endpoint
curl "https://localhost:7085/odata/Transactions?\$top=5"
```

**Issue**: Dockets not being sealed
**Solution**: Check Validator Service integration. Ensure DocketConfirmed events are being published.

**Issue**: Query performance is slow
**Solution**: Verify database indexes are created. Enable query result caching with Redis.

```json
{
  "Register": {
    "EnableQueryCache": true,
    "QueryCacheTTL": "00:05:00"
  }
}
```

### Debug Mode

Enable detailed logging:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Sorcha.Register.Service": "Trace",
      "Sorcha.Register.Core": "Trace"
    }
  }
}
```

---

## Contributing

### Development Workflow

1. **Create a feature branch**: `git checkout -b feature/your-feature`
2. **Make changes**: Follow C# coding conventions
3. **Write tests**: Maintain >90% coverage
4. **Run tests**: `dotnet test`
5. **Format code**: `dotnet format`
6. **Commit**: `git commit -m "feat: your feature description"`
7. **Push**: `git push origin feature/your-feature`
8. **Create PR**: Reference issue number

### Code Standards

- Follow [C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use async/await for I/O operations
- Add XML documentation for public APIs
- Include unit tests for all business logic
- Use dependency injection for testability

---

## Inbound Transaction Routing

The Register Service participates in the **inbound transaction routing** pipeline, identifying transactions that involve locally-registered wallet addresses and triggering notifications to the Wallet Service for user delivery.

### Bloom Filter (Local Address Index)

A Redis-backed probabilistic data structure efficiently determines whether a wallet address is registered locally. When a new docket is sealed, the bloom filter is queried for each transaction's recipient addresses to identify inbound transactions.

**Configuration** (`appsettings.json`):

| Setting | Default | Description |
|---------|---------|-------------|
| `BloomFilter:ExpectedAddressCount` | `100000` | Expected number of addresses (sizing parameter) |
| `BloomFilter:FalsePositiveRate` | `0.001` | Target false positive rate (0.1%) |
| `BloomFilter:RebuildOnStartup` | `true` | Rebuild the filter from storage on service start |

```json
{
  "BloomFilter": {
    "ExpectedAddressCount": 100000,
    "FalsePositiveRate": 0.001,
    "RebuildOnStartup": true
  }
}
```

### Admin Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/admin/registers/{registerId}/rebuild-index` | Force rebuild the bloom filter index for a register |

### Health Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/health/sync` | Returns recovery/sync status |

**Response format:**

```json
{
  "status": "synced",
  "currentDocket": 42,
  "targetDocket": 42,
  "progressPercentage": 100.0,
  "docketsProcessed": 42,
  "lastError": null
}
```

Status values: `synced` (fully caught up), `recovering` (processing missing dockets), `stalled` (recovery error).

### Recovery

The Register Service automatically detects docket gaps on startup and recovers missing data:

1. **Gap Detection**: Compares local docket height against peers to identify missing dockets
2. **Streaming Recovery**: Streams missing dockets from the Peer Service via gRPC
3. **Chain Integrity**: Verifies hash chain continuity during recovery
4. **Catch-up Notifications**: Delivers inbound transaction notifications for recovered dockets

Recovery runs as a `BackgroundService` and reports status via the `/health/sync` endpoint.

### gRPC Services

**RegisterAddressGrpcService** — Manages the local address index for inbound transaction routing.

| RPC Method | Description |
|------------|-------------|
| `RegisterLocalAddress` | Add an address to the bloom filter index |
| `RemoveLocalAddress` | Remove an address from the bloom filter index |
| `RebuildAddressIndex` | Rebuild the entire bloom filter from storage |

---

## DevMode posture and crypto-policy updates

A register's DevMode posture (plaintext payloads vs mandatory field-level encryption) is **set once at
genesis** and may only ever be promoted **DevMode → Normal**, never the reverse.

### Every control transaction must be submitted through the Validator

`CryptoPolicyService.SubmitPolicyUpdateAsync` is the single producer of `CryptoPolicyUpdate` control
transactions, used by both `POST /api/registers/{id}/disable-dev-mode` and
`POST /api/registers/{id}/governance/crypto-policy`. It submits via
`IValidatorServiceClient.SubmitTransactionAsync`, exactly like genesis ingestion, register creation
and system-register publish.

**Never write a control transaction with `TransactionManager.StoreTransactionAsync`.** That is a
direct store write, and it is the shape of a bug that shipped and stayed invisible:

- `DocketBuilder.BuildDocketAsync` builds dockets **solely** from `IVerifiedTransactionQueue`, which
  only Validator submission populates. A directly-stored transaction is therefore never sealed.
- Because it never seals, the docket-write projection in `Program.cs` that flips `Register.DevMode`
  never fires, and the Validator's one-way `DevMode` guard
  (`ControlDocketProcessor.ValidateCryptoPolicyUpdate`) never runs.
- The row still appears in Mongo and the endpoint still returns `200 {"status":"submitted"}`. The
  admin UI reported success. Unit tests were green, because they covered a `RegisterManager` helper
  that no shipped code path called. Nothing asserted the join between the two sides.

The failure is visible only by checking whether the transaction landed in a **docket**:

```javascript
// mongosh — the control tx must appear in a docket's TransactionIds, not just in `transactions`
db = db.getSiblingDB("sorcha_register_<registerId>");
db.transactions.find({"MetaData.TrackingData.transactionType": "CryptoPolicyUpdate"}, {TxId: 1});
db.dockets.find({}, {DocketNumber: 1, State: 1, TransactionIds: 1});
```

### Three details that silently break the submission

| Detail | Why it matters |
|---|---|
| `ActionId` = `control.crypto.update` | `ControlDocketProcessor.GetControlActionType` matches this exact string. Anything else seals as an ordinary action and the policy is never applied. |
| `Metadata["Type"]="Control"` **and** `Metadata["transactionType"]="CryptoPolicyUpdate"` | The docket-write DevMode projection requires **both** (the first resolves `MetaData.TransactionType`; the whole dictionary is copied onto `TrackingData`). Drop either and the promotion seals but does nothing. |
| Signatures encoded **base64url** | The Validator decodes base64url. Plain base64 differs only for some byte values, so it fails intermittently and reads as a signature bug. |

The payload hash must be computed over the canonical JSON form (`WriteIndented=false`,
`UnsafeRelaxedJsonEscaping`) that `ValidationEngine.ValidatePayloadHash` recomputes — a divergence is
a fatal `VAL_HASH_001` rejection far from its cause.

### Promotion is asynchronous, and not retrospective

`POST /disable-dev-mode` returns `200 … "status":"submitted"`. `Register.DevMode` flips on each node
as it writes the sealed docket, not when the call returns — poll `GET /api/registers/{id}`.

Transactions sealed while the register was in DevMode **stay plaintext forever**; promotion changes
the posture for new payloads only. And on an encrypted register, disclosure-group **field names**
(`disclosedFields`) remain in the clear by design — only values are ciphertext.

`PUT /api/registers/{id}/devmode` **has been removed.** It flipped the flag directly, in both
directions, with no control transaction — so it could revert a Normal register to plaintext, it never
replicated, and the one-way guard never saw it. Do not reintroduce a direct-write toggle.

---

## Register governance (Feature 189)

A governance change moves through **three kinds of transaction**, and only one of them changes the
roster. Which one is decided by the payload, not by a flag.

| Transaction | Payload | Carries a roster? | Moves the roster head? |
|---|---|---|---|
| **Proposal** | `ControlTransactionPayload` with `Operation.Status = Pending` | **no** (`roster: null`) | no |
| **Approval** | `GovernanceApprovalActionPayload` | no | no |
| **Enactment** | `ControlTransactionPayload` with `Operation.Status = Recorded` and `EnactsProposalId` | yes | yes |

A proposal carrying no roster is what makes the whole thing work. `GovernanceRosterService`
reconstructs the roster by walking control transactions newest-first and **skipping any whose payload
has no populated roster** — so a pending proposal is invisible to that reconstruction, and
`LastControlTxId` stays on the last real roster commit. That matters because FR-011b invalidates a
proposal whose `RosterSnapshotId` no longer matches `LastControlTxId`: a proposal that wrote a roster
would move that value and **invalidate itself the instant it sealed**, which is exactly what the
pre-split propose-and-enact transaction did.

**The Owner override keeps its single transaction.** Where quorum is met at raise time — a
single-owner register, which is the overwhelmingly common case — `/propose` still returns `200` and
writes one propose-and-enact transaction, unchanged. The split applies only where approvals are
genuinely required.

### Which key signs what

| Slot | Context | Whose key | Use for |
|---|---|---|---|
| 100 | `sorcha:register-attestation` | the **organisation** | genesis attestations, governance control transactions, approvals |
| 101 | `sorcha:register-control` | the **node** | node-internal control operations only |
| 102 | `sorcha:docket-signing` | the validator | sealing dockets |

A register's roster is built from its genesis attestations, which record the **organisation's**
slot-100 key. The node is never on a roster, so a node-signed governance transaction is refused
`submitter not found in roster` on any register whose genesis has sealed — accepted by the endpoint,
then rejected at the validator, which puts the error a long way from its cause. Route governance
signing through `IGovernanceSigningService`, never `ISystemWalletSigningService`.

### An approval's signature is not the transaction's signature

Two signatures are in play and they cover different things:

- **Authority** — the approver signs `GovernanceApprovalStatement`: the register, the whole
  operation, who is approving, and approve-versus-reject. This lives in the **payload**.
- **Carry** — the node signs `SHA-256("{txId}:{payloadHash}")`, which is what `Signatures` holds and
  what the validator verifies. Recorded as `Metadata["carriedBy"]`. It confers no authority.

An approver signs before any transaction exists, so they cannot sign an envelope. Filing the detached
signature in `Signatures` fails `VAL_SIG_002` every time, with a message that blames the approver.

The Owner override is gated on the **operation's proposer**, never on who signed the transaction.
Those coincide only while the proposer signs its own enacting transaction; once a reaction carries the
enactment, an Owner-signed carry would otherwise take the override and skip the quorum check
entirely — laundering a never-approved proposal through the carry.

### Enactment fires from a sealed docket

`GovernanceEnactmentSubscriber` watches `docket:confirmed` and re-evaluates **every open proposal** on
that register. It re-scans rather than reading the docket's own transactions because that is
self-healing: a dropped event costs a delay until the register's next docket, not a proposal stuck at
quorum for ever. It decides whether to *try*; the validator re-counts from the sealed approvals and
verifies every signature, so it is the authority.

Its transaction id is deterministic, so several nodes may submit and the duplicates dedupe rather than
fork — **which only holds while the payload bytes match**. Nothing in an enactment payload may read
the clock; every timestamp derives from `operation.ProposedAt`.

### Two traps worth knowing before you touch the payload

Both were found by the first live run, after a fully green suite, and both were silent.

1. **Serialisation must survive re-serialisation.** The validator re-serialises the payload and
   recomputes the hash. `GovernanceApprovalActionPayload.CanonicalJsonOptions` must keep
   `UnsafeRelaxedJsonEscaping`: the default encoder escapes `+`, so an approval whose base64 signature
   happened to contain one was refused `TX_012` while one whose bytes did not sealed normally.
2. **Read with the options you wrote with.** The payload's enums are kebab-case on the wire. Reading
   it back with ad-hoc options throws, and every reader treats a throw as "not an approval" — so both
   the enactment service and the validator counted **zero** approvals and nothing was ever enacted,
   with no error logged anywhere.

### Endpoints

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/registers/{id}/governance/roster` | Current roster, reconstructed from the control chain |
| GET | `/api/registers/{id}/governance/history` | Control transactions |
| POST | `/api/registers/{id}/governance/propose` | Raise an operation. `200` when the Owner override enacts it outright, `202` when it is recorded pending approval |
| GET | `/api/registers/{id}/governance/proposals` | List proposals |
| GET | `/api/registers/{id}/governance/proposals/{proposalId}/signing-request` | What an approver must sign. **Carries no digest** (FR-028) — the client derives it from the operation it rendered |
| POST | `/api/registers/{id}/governance/proposals/{proposalId}/approve` | Submit a detached approval. `202` accepted; `400` bad signature; `403` not on the roster or out of scope; `409` not open, expired or roster-changed; `422` malformed authorisation |

`202` means **accepted**, never **enacted**. A governance change takes effect when its transaction
seals into a docket — check the docket and the validator verdict, not the response body.

---

## Resources

- **Specification**: [.specify/specs/sorcha-register-service.md](https://github.com/Sorcha-Platform/Sorcha/blob/master/.specify/specs/sorcha-register-service.md)
- **API Reference**: [Scalar UI](https://localhost:7085/scalar)
- **Architecture**: [docs/architecture.md](../../../docs/architecture.md)
- **Development Status**: [docs/development-status.md](../../../docs/reference/development-status.md)
- **Transaction Format**: [docs/blockchain-transaction-format.md](../../../docs/reference/blockchain-transaction-format.md)
- **OpenAPI Spec**: `https://localhost:7085/openapi/v1.json`

---

## Technology Stack

**Runtime:**
- .NET 10.0 (10.0.100)
- C# 13
- ASP.NET Core 10

**Frameworks:**
- Minimal APIs for REST endpoints
- OData V4 for advanced queries
- SignalR for real-time notifications
- .NET Aspire 13.0+ for orchestration

**Storage:**
- **Primary**: MongoDB 7.0+ for document storage (`IRegisterRepository` is F113-audited — fails fast on an in-memory backend in Production/Staging)
- **Testing**: in-memory provider (non-production only)
- **Caching**: Redis for distributed caching

**Observability:**
- OpenTelemetry for distributed tracing
- Serilog for structured logging
- Prometheus metrics

**Testing:**
- xUnit for test framework
- FluentAssertions for assertions
- Testcontainers for integration tests (planned)

---

## License

Apache License 2.0 - See [LICENSE](https://github.com/Sorcha-Platform/Sorcha/blob/master/LICENSE) for details.

---

**Last Updated**: 2026-03-24
**Maintained By**: Sorcha Contributors
**Status**: ✅ Production Ready (100% Complete)
