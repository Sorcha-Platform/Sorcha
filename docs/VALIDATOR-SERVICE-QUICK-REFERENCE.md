# SiccaV3 Validator Service - Quick Reference

## Key Files Location

```
/src/Services/Validator/
├── ValidatorService/
│   ├── Controllers/ValidatorController.cs      (REST APIs)
│   ├── Startup.cs                              (DI setup)
│   └── appsettings.json                        (Config)
├── ValidationEngine/
│   ├── RulesBasedValidator.cs                  (Main validator loop)
│   ├── DocketBuilder.cs                        (Block builder)
│   ├── Genesys.cs                              (Genesis blocks)
│   ├── MemPool.cs                              (Transaction queue)
│   └── SingularConsensus.cs                    (Consensus stub)
├── ValidatorCore/
│   ├── ISorchaValidator.cs                     (Validator interface)
│   ├── ISorchaConsensus.cs                     (Consensus interface)
│   └── IMemPool.cs                             (MemPool interface)
└── ValidatorTests/
    └── RuleBasedValidatorTests.cs
```

## Code Statistics

- **Total Lines**: ~788 C# code across all modules
- **Main Classes**: 5 (RulesBasedValidator, DocketBuilder, MemPool, Genesys, SingularConsensus)
- **Key Interfaces**: 3 (ISorchaValidator, ISorchaConsensus, IMemPool)
- **Framework**: .NET 9.0 with ASP.NET Core

## Data Models Summary

### Docket (Block)
```csharp
{
  Id: ulong,                        // Block height
  RegisterId: string,               // Register GUID
  Hash: string,                     // SHA256 block hash
  PreviousHash: string,             // Chain link
  TransactionIds: List<string>,     // Transaction hashes
  Votes: string,                    // Voting data (placeholder)
  TimeStamp: DateTime,              // UTC creation
  State: DocketState,               // Init|Proposed|Accepted|Rejected|Sealed
  MetaData: TransactionMetaData     // Type = Docket
}
```

### TransactionModel
```csharp
{
  TxId: string,                     // 64-char hex hash (ID)
  PrevTxId: string,                 // Previous tx link
  Version: uint,
  SenderWallet: string,             // Base58 address
  RecipientsWallets: IEnumerable<string>,
  TimeStamp: DateTime,
  MetaData: TransactionMetaData,    // Type, blueprint, instance
  Payloads: PayloadModel[],         // Encrypted data
  Signature: string                 // Crypto signature
}
```

### Register
```csharp
{
  Id: string,                       // GUID (no hyphens)
  Name: string,                     // Display name
  Height: uint,                     // Current block height
  Status: RegisterStatusTypes,      // OFFLINE|ONLINE|CHECKING|RECOVERY
  Advertise: bool,
  IsFullReplica: bool,
  Dockets: IEnumerable<Docket>,
  Transactions: IEnumerable<TransactionModel>
}
```

## Processing Flow

```
1. Transaction arrives via Dapr pub/sub (OnTransaction_Pending)
   ↓
2. ValidatorController.ReceiveTx() adds to MemPool
   ↓
3. Every 10 seconds: RulesBasedValidator.ProcessValidation()
   ├─ Check each register height
   ├─ If height < 1: Genesys.ProcessGenesys() creates genesis block (state=Sealed)
   ├─ Else: Get transactions from MemPool
   ├─ Call DocketBuilder.GenerateDocket() to create block
   ├─ Publish to OnDocket_Confirmed topic
   └─ Clear MemPool and publish OnTransaction_ValidationCompleted
   ↓
4. RegisterService receives docket via pub/sub
   ├─ Store in database
   ├─ Increment register height
   └─ Publish finalization events
   ↓
5. SingularConsensus.ProcessConsent() - Currently does NOTHING
```

## Configuration

```json
{
  "Validator": {
    "CycleTime": 10     // Validation cycle in seconds
  },
  "Logging": {
    "LogLevel": { "Default": "Information" }
  },
  "ApplicationInsights": { /* telemetry */ },
  "TenantIssuer": "http://localhost",  // JWT authority
  "DaprSecret": ""                     // Inter-service auth
}
```

## Pub/Sub Topics

| Topic | Direction | Use |
|-------|-----------|-----|
| OnTransaction_Pending | In | Receive transactions from PeerService |
| OnDocket_Confirmed | Out | Send dockets to RegisterService |
| OnTransaction_ValidationCompleted | Out | Notify wallets of finalization |

## APIs

### Administrative Endpoints

```
GET  /api/Validators                    # Status (returns register list)
GET  /api/Validators/{transactionId}    # Transaction from state store
POST /api/Validators                    # Event handler (Dapr pub/sub)
GET  /api/Validators/healthz           # Health check
```

### Authorization

- JWT Bearer tokens from TenantService
- Roles: installation.admin, installation.reader, register.maintainer
- Dapr secret authentication for inter-service calls

## Key Design Decisions

### What Works Well
1. ✓ Clean layered architecture
2. ✓ Event-driven via Dapr pub/sub
3. ✓ Thread-safe MemPool implementation
4. ✓ Genesis block creation
5. ✓ Per-register processing isolation
6. ✓ SHA256 hashing for chain integrity

### What's Missing/Broken
1. ✗ Consensus mechanism is a stub (not implemented)
2. ✗ Validation is minimal (no signature checking)
3. ✗ PreviousHash is fixed placeholder
4. ✗ No chain recovery mechanism
5. ✗ No rate limiting or transaction limits
6. ✗ No fork detection
7. ✗ No rollback support

## Consensus Design Issue

**Current State**: SingularConsensus is completely empty:
```csharp
private async void ProcessConsent(object? state)
{
    await Task.Run(() => {});  // Does nothing!
}
```

**Intent**: Should transition dockets through consensus states:
- Proposed → Accepted → Sealed

**Current Workaround**: RegisterService accepts dockets directly without consensus voting

## Thread Safety

**MemPool** uses lock pattern:
```csharp
private readonly object _locker = new();

public void AddTxToPool(TransactionModel tx)
{
    lock (_locker) { /* add to dictionary */ }
}

public List<TransactionModel> GetPool(string registerId)
{
    lock (_locker) { /* get and remove */ }
}
```

This ensures HTTP receiver and validation timer can safely operate concurrently.

## Cryptography

### Implemented
- SHA256 hashing for blocks (via ComputeSha256Hash static method)
- Transaction signatures (created elsewhere, not verified)

### Not Implemented
- Signature verification in validator
- Key management/rotation
- Payload encryption validation
- Challenge verification

## Performance Characteristics

- **Validation Cycle**: 10 seconds (configurable)
- **Throughput**: ~100-1000 tx/sec per register (depends on RegisterService)
- **Latency**: Transaction to finalization ~15-20 seconds
- **MemPool**: Unbounded size (potential issue)
- **Scalability**: Single-node only (no clustering)

## Dependencies

### NuGet Packages
- Dapr.Client (1.14.0) - Distributed patterns
- Dapr.AspNetCore (1.14.0) - Dapr pub/sub integration
- Serilog.AspNetCore (8.0.3) - Logging
- Microsoft.AspNetCore.Authentication.JwtBearer - JWT auth
- FluentValidation - Model validation

### Services
- RegisterService (stores dockets, maintains height)
- PeerService (sources transactions)
- TenantService (JWT issuer)
- WalletService (signature creation - not used by validator)

## Deployment

### Docker
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0
COPY --from=build /app
ENTRYPOINT ["dotnet", "ValidatorService.dll"]
```

### Environment Variables
- DAPR_GRPC_PORT (default 50001)
- DAPR_HTTP_PORT (default 3500)
- Validator__CycleTime (override config)

### Health Check
- **Endpoint**: /api/Validators/healthz
- **Interval**: Configurable in Kubernetes/Docker
- **Checks**: Service responsiveness, Dapr connectivity, RegisterService reachability

## Logging

### Structured Logging (Serilog)
- **Format**: JSON to Application Insights
- **Level**: Information (or Debug in development)
- **Context**: Includes MachineName, ThreadId, Application="ValidatorService"
- **Excludes**: /healthz requests (to reduce noise)

### Key Log Messages
- "[VALIDATOR] Started/Stopped"
- "[NEWDOCKET] Register: {rid}, Docket: {did}, Tx Count: {tid}"
- "Docket created, Id: {id} Hash: {hash}"
- "[GENESYS] Register: {id}"
- "Processing Register: {id}" / "Completed Register: {id}"

## Testing

### Unit Tests Location
- `/src/Services/Validator/ValidatorTests/RuleBasedValidatorTests.cs`

### Current Test Status
- Test class exists but is EMPTY (just a stub with [Fact] public void Test1())
- **Recommendation**: Add comprehensive tests for:
  - Docket generation
  - MemPool operations
  - Genesis block creation
  - Multi-register processing
  - Consensus transitions (once implemented)

## TODOs in Codebase

1. **Height Recovery**: 
   ```csharp
   //todo: when we have network, ask the Network for the height first - this might be a recovery.
   ```

2. **Consensus Finalization** (in DocketController):
   ```csharp
   // OK this is just for the moment as we WOULD NOT WRITE THIS STRAIGHT TO STORAGE!!!
   ```

3. **Voting Mechanism**:
   ```csharp
   // Not sure why this is here!!
   ```

## Integration Points

### Incoming
- **From PeerService**: Transactions via OnTransaction_Pending topic

### Outgoing
- **To RegisterService**: Dockets via OnDocket_Confirmed topic
- **To WalletService/Clients**: Confirmations via OnTransaction_ValidationCompleted topic

### State Management
- **Dapr State Store**: Caches transaction state (optional)
- **RegisterService Database**: Persistent docket storage
- **In-Memory**: MemPool, register list, validation state

## Security Notes

### Implemented
- JWT token validation with tenant claim
- Role-based access control
- Dapr secret authentication for inter-service calls
- No exposure of private keys or sensitive data

### Not Implemented
- Transaction signature verification
- Rate limiting
- Input sanitization beyond model validation
- Payload encryption validation
- DoS protection (unbounded mempool)

## Future Evolution Path (Implied by Architecture)

1. **Phase 1** (Current): Single-node, minimal validation, stub consensus
2. **Phase 2** (Implied): Multi-node support, real consensus, transaction validation
3. **Phase 3** (Implied): Byzantine fault tolerance, sharding, interoperability

The architecture is designed to support these enhancements with minimal refactoring.

---

## Quick Debugging Tips

### Enable Debug Logging
```json
"Serilog": {
  "MinimumLevel": "Debug"
}
```

### Check MemPool State
```csharp
var pool = serviceProvider.GetRequiredService<IMemPool>();
var count = pool.RegisterCount();  // Number of registers with pending tx
var registries = pool.GetPool("register-id");  // Get all tx for register
```

### Monitor Docket Creation
Look for logs containing "[NEWDOCKET]" or "Docket created" with:
- RegisterId
- DocketId (Height)
- TransactionIds count
- Hash value

### Consensus Health
Check SingularConsensus logs - currently should be empty since it does nothing.

---

## Additional Resources

- Full Analysis: `./alpha-validator-service-analysis.md` (1035 lines, alpha reference)
- Design Recommendations: `./SORCHA-VALIDATOR-DESIGN-RECOMMENDATIONS.md`
- GitHub: https://github.com/sorcha-platform/sorcha

